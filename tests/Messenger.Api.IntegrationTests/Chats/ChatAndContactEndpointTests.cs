using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Messenger.Api.IntegrationTests.Auth;
using Messenger.Contracts.Auth;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Contacts;
using Messenger.Contracts.Users;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Messenger.Api.IntegrationTests.Chats;

public sealed class ChatAndContactEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("messenger_social_tests")
        .WithUsername("messenger")
        .WithPassword("messenger-tests-only")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Contact_discovery_is_exact_private_and_case_insensitive_username_remains_unique()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "+79990000301");
        var alice = await RegisterAsync(client, "+79990000302");
        var alicia = await RegisterAsync(client, "+79990000303");
        await UpdateProfileAsync(client, alice, "Alice", "A", "Alice");
        await UpdateProfileAsync(client, alicia, "Alicia", "B", "Alicia");
        await AuthorizeAsync(client, alice);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/me/privacy",
            new PrivacyRequest("nobody", "everybody", "nobody"))).StatusCode);

        await AuthorizeAsync(client, owner);
        var partial = await client.GetFromJsonAsync<UserSearchPageResponse>("/api/users/search?query=ali");
        var exactName = await client.GetFromJsonAsync<UserSearchPageResponse>("/api/users/search?query=ALICE");
        var exactPhone = await client.GetFromJsonAsync<UserSearchPageResponse>("/api/users/search?query=%2B79990000302");

        Assert.Empty(partial!.Items);
        Assert.Equal(alice.UserId, Assert.Single(exactName!.Items).UserId);
        Assert.Equal(alice.UserId, Assert.Single(exactPhone!.Items).UserId);
        Assert.Null(exactPhone.Items[0].Phone);

        await AuthorizeAsync(client, alicia);
        var duplicateUsername = await client.PatchAsJsonAsync("/api/me",
            new UpdateMeRequest("Alicia", "B", "aLiCe", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicateUsername.StatusCode);
    }

    [Fact]
    public async Task Contacts_use_stable_cursor_pages_and_foreign_contact_ids_return_not_found()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "+79990000311");
        var first = await RegisterAsync(client, "+79990000312");
        var second = await RegisterAsync(client, "+79990000313");
        var third = await RegisterAsync(client, "+79990000314");
        var attacker = await RegisterAsync(client, "+79990000315");
        await UpdateProfileAsync(client, first, "Первый", "", "first_contact");
        await UpdateProfileAsync(client, second, "Второй", "", "second_contact");
        await UpdateProfileAsync(client, third, "Третий", "", "third_contact");

        await AuthorizeAsync(client, owner);
        var created = new List<ContactResponse>();
        foreach (var target in new[] { first, second, third })
        {
            var response = await client.PostAsJsonAsync("/api/contacts",
                new CreateContactRequest(target.UserId, "Локальное", null));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            created.Add((await response.Content.ReadFromJsonAsync<ContactResponse>())!);
        }

        var firstPage = await client.GetFromJsonAsync<ContactPageResponse>("/api/contacts?limit=2");
        var secondPage = await client.GetFromJsonAsync<ContactPageResponse>(
            $"/api/contacts?limit=2&cursor={Uri.EscapeDataString(firstPage!.NextCursor!)}");
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Empty(firstPage.Items.Select(item => item.Id).Intersect(secondPage.Items.Select(item => item.Id)));

        await AuthorizeAsync(client, attacker);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsJsonAsync(
            $"/api/contacts/{created[0].Id}", new UpdateContactRequest("Украдено", null, true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/contacts/{created[0].Id}")).StatusCode);
    }

    [Fact]
    public async Task Direct_groups_member_permissions_and_hidden_reactivation_work()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "+79990000321");
        var admin = await RegisterAsync(client, "+79990000322");
        var member = await RegisterAsync(client, "+79990000323");
        var outsider = await RegisterAsync(client, "+79990000324");

        await AuthorizeAsync(client, owner);
        var firstDirect = await PostAsync<ChatResponse>(client, "/api/chats/direct", new CreateDirectChatRequest(admin.UserId));
        var sameDirect = await PostAsync<ChatResponse>(client, "/api/chats/direct", new CreateDirectChatRequest(admin.UserId));
        Assert.Equal(firstDirect.Id, sameDirect.Id);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/chats/{firstDirect.Id}")).StatusCode);
        var reactivated = await PostAsync<ChatResponse>(client, "/api/chats/direct", new CreateDirectChatRequest(admin.UserId));
        Assert.Equal(firstDirect.Id, reactivated.Id);
        Assert.False(reactivated.IsHidden);

        var group = await PostAsync<ChatResponse>(client, "/api/chats/groups",
            new CreateGroupChatRequest("Команда", [admin.UserId, member.UserId]));
        Assert.Equal("owner", group.Role);

        await AuthorizeAsync(client, admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/chats/{group.Id}",
            new UpdateGroupChatRequest("Чужое название", null))).StatusCode);

        await AuthorizeAsync(client, owner);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync(
            $"/api/chats/{group.Id}/members/{admin.UserId}", new UpdateChatMemberRequest("admin"))).StatusCode);

        await AuthorizeAsync(client, admin);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/chats/{group.Id}/members/{member.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"/api/chats/{group.Id}/members/{owner.UserId}")).StatusCode);

        await AuthorizeAsync(client, owner);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await client.DeleteAsync($"/api/chats/{group.Id}/members/{owner.UserId}")).StatusCode);

        await AuthorizeAsync(client, outsider);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/chats/{group.Id}")).StatusCode);
    }

    [Fact]
    public async Task Archive_mute_read_and_folder_order_are_owner_scoped()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client, "+79990000331");
        var target = await RegisterAsync(client, "+79990000332");
        await AuthorizeAsync(client, owner);
        var chat = await PostAsync<ChatResponse>(client, "/api/chats/direct", new CreateDirectChatRequest(target.UserId));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/chats/{chat.Id}/archive", new ChatFlagRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/chats/{chat.Id}/mute", new ChatFlagRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/chats/{chat.Id}/read", new ReadChatRequest(0))).StatusCode);

        var first = await PostAsync<ChatFolderResponse>(client, "/api/chat-folders", new CreateChatFolderRequest("Первая"));
        var second = await PostAsync<ChatFolderResponse>(client, "/api/chat-folders", new CreateChatFolderRequest("Вторая"));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            "/api/chat-folders/order", new ReorderChatFoldersRequest([second.Id, first.Id]))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/chat-folders/{second.Id}/chats", new SetChatFolderChatsRequest([chat.Id]))).StatusCode);

        var folders = await client.GetFromJsonAsync<List<ChatFolderResponse>>("/api/chat-folders")
            ?? throw new InvalidDataException("Missing folder response.");
        Assert.Equal([second.Id, first.Id], folders.Select(folder => folder.Id));
        Assert.Equal([chat.Id], folders[0].ChatIds);

        await AuthorizeAsync(client, target);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
            $"/api/chat-folders/{second.Id}/chats", new SetChatFolderChatsRequest([]))).StatusCode);
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
        var challengeResponse = await client.PostAsJsonAsync("/api/auth/challenges",
            new ChallengeRequest(phone, "RU", "register"));
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<ChallengeResponse>();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new CompleteChallengeRequest(challenge!.ChallengeId, "111111", "Tests"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
    }

    private static async Task UpdateProfileAsync(
        HttpClient client,
        AuthSessionResponse session,
        string firstName,
        string lastName,
        string username)
    {
        await AuthorizeAsync(client, session);
        var response = await client.PatchAsJsonAsync("/api/me",
            new UpdateMeRequest(firstName, lastName, username, null));
        response.EnsureSuccessStatusCode();
    }

    private static Task AuthorizeAsync(HttpClient client, AuthSessionResponse session)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return Task.CompletedTask;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string uri, object request)
    {
        var response = await client.PostAsJsonAsync(uri, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
