using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Messenger.Api.IntegrationTests.Auth;
using Messenger.Contracts.Auth;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Messages;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Messenger.Api.IntegrationTests.Chats;

public sealed class MessageEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("messenger_message_tests")
        .WithUsername("messenger")
        .WithPassword("messenger-tests-only")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Send_is_idempotent_and_message_cursor_is_stable()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var sender = await RegisterAsync(client, "+79990000401");
        var recipient = await RegisterAsync(client, "+79990000402");
        Authorize(client, sender);
        var chat = await PostAsync<ChatResponse>(client, "/api/chats/direct", new CreateDirectChatRequest(recipient.UserId));
        var clientId = Guid.NewGuid();

        var first = await PostAsync<MessageResponse>(client, $"/api/chats/{chat.Id}/messages",
            new SendMessageRequest(clientId, "text", "Первое секретное"));
        var retry = await PostAsync<MessageResponse>(client, $"/api/chats/{chat.Id}/messages",
            new SendMessageRequest(clientId, "text", "Игнорируется"));
        var second = await PostAsync<MessageResponse>(client, $"/api/chats/{chat.Id}/messages",
            new SendMessageRequest(Guid.NewGuid(), "text", "Второе"));

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
        var firstPage = await client.GetFromJsonAsync<MessagePageResponse>($"/api/chats/{chat.Id}/messages?limit=1");
        var secondPage = await client.GetFromJsonAsync<MessagePageResponse>(
            $"/api/chats/{chat.Id}/messages?limit=1&cursor={firstPage!.NextCursor}");
        Assert.Equal(first.Id, Assert.Single(firstPage.Items).Id);
        Assert.Equal(second.Id, Assert.Single(secondPage!.Items).Id);
    }

    [Fact]
    public async Task Database_enforces_sender_client_id_uniqueness_under_concurrent_requests()
    {
        await using var factory = await CreateFactoryAsync();
        var setup = factory.CreateClient();
        var sender = await RegisterAsync(setup, "+79990000411");
        var recipient = await RegisterAsync(setup, "+79990000412");
        Authorize(setup, sender);
        var chat = await PostAsync<ChatResponse>(setup, "/api/chats/direct", new CreateDirectChatRequest(recipient.UserId));
        var firstClient = factory.CreateClient();
        var secondClient = factory.CreateClient();
        Authorize(firstClient, sender);
        Authorize(secondClient, sender);
        var request = new SendMessageRequest(Guid.NewGuid(), "text", "Один раз");

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync($"/api/chats/{chat.Id}/messages", request),
            secondClient.PostAsJsonAsync($"/api/chats/{chat.Id}/messages", request));

        Assert.All(responses, response => Assert.True(
            response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict,
            $"Unexpected status: {response.StatusCode}"));
        Assert.Contains(responses, response => response.IsSuccessStatusCode);
        var successfulMessages = new List<MessageResponse>();
        foreach (var response in responses.Where(response => response.IsSuccessStatusCode))
        {
            successfulMessages.Add((await response.Content.ReadFromJsonAsync<MessageResponse>())!);
        }
        Assert.Single(successfulMessages.Select(message => message.Id).Distinct());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessengerDbContext>();
        Assert.Equal(1, await db.Messages.CountAsync(message => message.SenderUserId == sender.UserId &&
            message.ClientMessageId == request.ClientMessageId));
    }

    [Fact]
    public async Task Search_edit_hide_delete_and_pin_obey_membership_and_ownership()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "+79990000421");
        var member = await RegisterAsync(client, "+79990000422");
        var outsider = await RegisterAsync(client, "+79990000423");
        Authorize(client, owner);
        var group = await PostAsync<ChatResponse>(client, "/api/chats/groups",
            new CreateGroupChatRequest("Группа", [member.UserId]));
        var message = await PostAsync<MessageResponse>(client, $"/api/chats/{group.Id}/messages",
            new SendMessageRequest(Guid.NewGuid(), "text", "Секретное точное слово"));

        var search = await client.GetFromJsonAsync<MessagePageResponse>(
            $"/api/chats/{group.Id}/messages/search?query=%D1%82%D0%BE%D1%87%D0%BD%D0%BE%D0%B5");
        Assert.Equal(message.Id, Assert.Single(search!.Items).Id);
        Assert.Empty((await client.GetFromJsonAsync<MessagePageResponse>(
            $"/api/chats/{group.Id}/messages/search?query=%D1%82%D0%BE%D1%87%D0%BD"))!.Items);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsync($"/api/messages/{message.Id}/pin", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PatchAsJsonAsync($"/api/messages/{message.Id}", new EditMessageRequest("Новое точное слово"))).StatusCode);

        Authorize(client, member);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsync($"/api/messages/{message.Id}/pin", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/messages/{message.Id}?globally=false")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<MessagePageResponse>($"/api/chats/{group.Id}/messages"))!.Items);

        Authorize(client, outsider);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/chats/{group.Id}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/chats/{group.Id}/messages/search?query=%D1%81%D0%BB%D0%BE%D0%B2%D0%BE")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/chats/{group.Id}/media")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsJsonAsync(
            $"/api/messages/{message.Id}", new EditMessageRequest("Украдено"))).StatusCode);
    }

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
