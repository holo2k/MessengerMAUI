using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using Messenger.Api.IntegrationTests.Auth;
using Messenger.Contracts.Auth;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Realtime;
using Messenger.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Messenger.Api.IntegrationTests.Realtime;

public sealed class SignalRTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("messenger_signalr_tests")
        .WithUsername("messenger")
        .WithPassword("messenger-tests-only")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Only_active_members_receive_message_created_after_the_message_is_committed()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var sender = await RegisterAsync(client, "+79990000501");
        var recipient = await RegisterAsync(client, "+79990000502");
        var outsider = await RegisterAsync(client, "+79990000503");
        Authorize(client, sender);
        var chat = await PostAsync<ChatResponse>(client, "/api/chats/direct", new CreateDirectChatRequest(recipient.UserId));
        var recipientEvents = Channel.CreateUnbounded<MessageCreatedEvent>();
        var outsiderEvents = Channel.CreateUnbounded<MessageCreatedEvent>();
        await using var recipientConnection = Connection(factory, recipient.AccessToken);
        await using var outsiderConnection = Connection(factory, outsider.AccessToken);
        recipientConnection.On<MessageCreatedEvent>("MessageCreated", value => recipientEvents.Writer.TryWrite(value));
        outsiderConnection.On<MessageCreatedEvent>("MessageCreated", value => outsiderEvents.Writer.TryWrite(value));
        await recipientConnection.StartAsync();
        await outsiderConnection.StartAsync();

        var sent = await PostAsync<MessageResponse>(client, $"/api/chats/{chat.Id}/messages",
            new SendMessageRequest(Guid.NewGuid(), "text", "Realtime"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var realtime = await recipientEvents.Reader.ReadAsync(timeout.Token);

        Assert.Equal(sent.Id, realtime.MessageId);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();
        Assert.True(await db.Messages.AnyAsync(message => message.Id == realtime.MessageId));
        await Task.Delay(150);
        Assert.False(outsiderEvents.Reader.TryRead(out _));
    }

    private static HubConnection Connection(MessengerApiFactory factory, string token) =>
        new HubConnectionBuilder().WithUrl("http://localhost/hubs/chat", options =>
        {
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.AccessTokenProvider = () => Task.FromResult<string?>(token);
        }).Build();

    private async Task<MessengerApiFactory> CreateFactoryAsync()
    {
        var factory = new MessengerApiFactory(_postgres.GetConnectionString(), "Development", 100, 100);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MessengerDbContext>().Database.MigrateAsync();
        return factory;
    }

    private static async Task<AuthSessionResponse> RegisterAsync(HttpClient client, string phone)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var challenge = await (await client.PostAsJsonAsync("/api/auth/challenges",
            new ChallengeRequest(phone, "RU", "register"))).Content.ReadFromJsonAsync<ChallengeResponse>();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new CompleteChallengeRequest(challenge!.ChallengeId, "111111", "Tests"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
    }

    private static void Authorize(HttpClient client, AuthSessionResponse session) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

    private static async Task<T> PostAsync<T>(HttpClient client, string uri, object request)
    {
        var response = await client.PostAsJsonAsync(uri, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
