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
        await recipientConnection.InvokeAsync("Ready");
        await outsiderConnection.InvokeAsync("Ready");

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

        var updates = Channel.CreateUnbounded<MessageUpdatedEvent>();
        var deletions = Channel.CreateUnbounded<MessageDeletedEvent>();
        var hidden = Channel.CreateUnbounded<MessageHiddenEvent>();
        var senderHidden = Channel.CreateUnbounded<MessageHiddenEvent>();
        var outsiderUpdates = Channel.CreateUnbounded<MessageUpdatedEvent>();
        var outsiderDeletions = Channel.CreateUnbounded<MessageDeletedEvent>();
        recipientConnection.On<MessageUpdatedEvent>("MessageUpdated", value => updates.Writer.TryWrite(value));
        recipientConnection.On<MessageDeletedEvent>("MessageDeleted", value => deletions.Writer.TryWrite(value));
        recipientConnection.On<MessageHiddenEvent>("MessageHidden", value => hidden.Writer.TryWrite(value));
        outsiderConnection.On<MessageUpdatedEvent>("MessageUpdated", value => outsiderUpdates.Writer.TryWrite(value));
        outsiderConnection.On<MessageDeletedEvent>("MessageDeleted", value => outsiderDeletions.Writer.TryWrite(value));
        await using var senderConnection = Connection(factory, sender.AccessToken);
        senderConnection.On<MessageHiddenEvent>("MessageHidden", value => senderHidden.Writer.TryWrite(value));
        await senderConnection.StartAsync();
        await senderConnection.InvokeAsync("Ready");
        using var changesTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        (await client.PatchAsJsonAsync($"/api/messages/{sent.Id}", new EditMessageRequest("Исправлено"))).EnsureSuccessStatusCode();
        Assert.Equal(new MessageUpdatedEvent(chat.Id, sent.Id, sent.Sequence), await updates.Reader.ReadAsync(changesTimeout.Token));
        Authorize(client, recipient);
        Assert.Equal("Исправлено", (await client.GetFromJsonAsync<MessageResponse>($"/api/messages/{sent.Id}"))!.Body);
        (await client.DeleteAsync($"/api/messages/{sent.Id}?globally=false")).EnsureSuccessStatusCode();
        Assert.Equal(new MessageHiddenEvent(chat.Id, sent.Id), await hidden.Reader.ReadAsync(changesTimeout.Token));
        Authorize(client, sender);
        (await client.DeleteAsync($"/api/messages/{sent.Id}?globally=true")).EnsureSuccessStatusCode();
        Assert.Equal(new MessageDeletedEvent(chat.Id, sent.Id, sent.Sequence), await deletions.Reader.ReadAsync(changesTimeout.Token));
        var deleted = await client.GetFromJsonAsync<MessageResponse>($"/api/messages/{sent.Id}");
        Assert.NotNull(deleted!.DeletedAt);
        Assert.Null(deleted.Body);
        Assert.False(senderHidden.Reader.TryRead(out _));
        Assert.False(outsiderUpdates.Reader.TryRead(out _));
        Assert.False(outsiderDeletions.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Read_receipts_are_committed_bounded_monotonic_and_only_visible_to_chat_members()
    {
        await using var factory = await CreateFactoryAsync();
        using var senderClient = factory.CreateClient();
        using var readerClient = factory.CreateClient();
        using var outsiderClient = factory.CreateClient();
        var sender = await RegisterAsync(senderClient, "+79990000511");
        var reader = await RegisterAsync(readerClient, "+79990000512");
        var outsider = await RegisterAsync(outsiderClient, "+79990000513");
        Authorize(senderClient, sender);
        Authorize(readerClient, reader);
        Authorize(outsiderClient, outsider);
        var receipts = Channel.CreateUnbounded<ReadPositionChangedEvent>();
        var outsiderReceipts = Channel.CreateUnbounded<ReadPositionChangedEvent>();
        await using var senderConnection = Connection(factory, sender.AccessToken);
        await using var outsiderConnection = Connection(factory, outsider.AccessToken);
        senderConnection.On<ReadPositionChangedEvent>("ReadPositionChanged", value => receipts.Writer.TryWrite(value));
        outsiderConnection.On<ReadPositionChangedEvent>("ReadPositionChanged", value => outsiderReceipts.Writer.TryWrite(value));
        await senderConnection.StartAsync();
        await outsiderConnection.StartAsync();
        await senderConnection.InvokeAsync("Ready");
        await outsiderConnection.InvokeAsync("Ready");

        var chat = await PostAsync<ChatResponse>(senderClient, "/api/chats/direct", new CreateDirectChatRequest(reader.UserId));
        await senderConnection.InvokeAsync("JoinChat", chat.Id);
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => outsiderConnection.InvokeAsync("JoinChat", chat.Id));
        await PostAsync<MessageResponse>(senderClient, $"/api/chats/{chat.Id}/messages", new SendMessageRequest(Guid.NewGuid(), "text", "First"));
        await PostAsync<MessageResponse>(senderClient, $"/api/chats/{chat.Id}/messages", new SendMessageRequest(Guid.NewGuid(), "text", "Second"));

        (await readerClient.PutAsJsonAsync($"/api/chats/{chat.Id}/read", new ReadChatRequest(1))).EnsureSuccessStatusCode();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receipt = await receipts.Reader.ReadAsync(timeout.Token);
        Assert.Equal(new ReadPositionChangedEvent(chat.Id, reader.UserId, 1), receipt);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();
            Assert.Equal(1, await db.ChatMembers.Where(member => member.ChatId == chat.Id && member.UserId == reader.UserId)
                .Select(member => member.LastReadSequence).SingleAsync());
        }
        var members = await senderClient.GetFromJsonAsync<ChatMemberResponse[]>($"/api/chats/{chat.Id}/members");
        Assert.Equal(1, members!.Single(member => member.UserId == reader.UserId).LastReadSequence);

        (await readerClient.PutAsJsonAsync($"/api/chats/{chat.Id}/read", new ReadChatRequest(0))).EnsureSuccessStatusCode();
        Assert.Equal(1, (await receipts.Reader.ReadAsync(timeout.Token)).Sequence);
        (await readerClient.PutAsJsonAsync($"/api/chats/{chat.Id}/read", new ReadChatRequest(long.MaxValue))).EnsureSuccessStatusCode();
        Assert.Equal(2, (await receipts.Reader.ReadAsync(timeout.Token)).Sequence);

        var concurrent = await Task.WhenAll(Enumerable.Range(0, 6).Select(index =>
            readerClient.PutAsJsonAsync($"/api/chats/{chat.Id}/read", new ReadChatRequest(index % 2 == 0 ? 1 : 2))));
        Assert.All(concurrent, response => Assert.True(response.IsSuccessStatusCode));
        members = await senderClient.GetFromJsonAsync<ChatMemberResponse[]>($"/api/chats/{chat.Id}/members");
        Assert.NotNull(members);
        Assert.Equal(2, members.Single(member => member.UserId == reader.UserId).LastReadSequence);
        Assert.Equal(0, members.Single(member => member.UserId == sender.UserId).LastReadSequence);

        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await outsiderClient.PutAsJsonAsync($"/api/chats/{chat.Id}/read", new ReadChatRequest(2))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await outsiderClient.GetAsync($"/api/chats/{chat.Id}/members")).StatusCode);
        Assert.False(outsiderReceipts.Reader.TryRead(out _));
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
