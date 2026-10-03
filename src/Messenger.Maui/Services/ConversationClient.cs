using System.Net.Http.Json;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Contacts;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Media;

namespace Messenger.Maui.Services;

public interface IConversationApi
{
    Task<IReadOnlyList<ChatResponse>> GetChatsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ChatFolderResponse>> GetFoldersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ContactResponse>> GetContactsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UserSearchResponse>> SearchUsersAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid chatId, long afterSequence, CancellationToken ct = default);
    Task ReorderFoldersAsync(IReadOnlyList<Guid> folderIds, CancellationToken ct = default);
    Task SetArchivedAsync(Guid chatId, bool value, CancellationToken ct = default);
    Task MarkReadAsync(Guid chatId, long sequence, CancellationToken ct = default);
    Task DeleteChatAsync(Guid chatId, CancellationToken ct = default);
    Task<ChatResponse> CreateDirectAsync(Guid userId, CancellationToken ct = default);
    Task<ChatResponse> CreateGroupAsync(string title, IReadOnlyList<Guid> userIds, CancellationToken ct = default);
    Task<MessageResponse> SendAsync(Guid chatId, Guid clientId, string body, CancellationToken ct = default);
    Task<MessageResponse> SendWithAttachmentsAsync(Guid chatId, Guid clientId, string type, string? body, IReadOnlyList<Guid> objectIds, CancellationToken ct = default);
    Task<IReadOnlyList<MessageResponse>> SearchAsync(Guid chatId, string query, CancellationToken ct = default);
    Task<IReadOnlyList<MessageResponse>> GetPinsAsync(Guid chatId, CancellationToken ct = default);
    Task<IReadOnlyList<StoredObjectResponse>> GetChatMediaAsync(Guid chatId, CancellationToken ct = default);
    Task<ContactResponse> UpdateContactAsync(Guid contactId, UpdateContactRequest request, CancellationToken ct = default);
}

public interface IPhoneDialer
{
    Task OpenAsync(string uri);
}

public sealed class ConversationClient(HttpClient httpClient) : IConversationApi
{
    public async Task<IReadOnlyList<ChatResponse>> GetChatsAsync(CancellationToken ct = default) =>
        (await httpClient.GetFromJsonAsync<ChatPageResponse>("api/chats?limit=100", ct))?.Items ?? [];

    public async Task<IReadOnlyList<ChatFolderResponse>> GetFoldersAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<List<ChatFolderResponse>>("api/chat-folders", ct) ?? [];

    public async Task<IReadOnlyList<ContactResponse>> GetContactsAsync(CancellationToken ct = default) =>
        (await httpClient.GetFromJsonAsync<ContactPageResponse>("api/contacts?limit=100", ct))?.Items ?? [];

    public async Task<IReadOnlyList<UserSearchResponse>> SearchUsersAsync(string query, CancellationToken ct = default) =>
        (await httpClient.GetFromJsonAsync<UserSearchPageResponse>(
            $"api/users/search?query={Uri.EscapeDataString(query)}", ct))?.Items ?? [];

    public async Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid chatId, long afterSequence, CancellationToken ct = default) =>
        (await httpClient.GetFromJsonAsync<MessagePageResponse>(
            $"api/chats/{chatId}/messages?cursor={afterSequence}&limit=99", ct))?.Items ?? [];

    public async Task ReorderFoldersAsync(IReadOnlyList<Guid> folderIds, CancellationToken ct = default) =>
        await EnsureAsync(await httpClient.PutAsJsonAsync("api/chat-folders/order", new ReorderChatFoldersRequest(folderIds), ct));

    public async Task SetArchivedAsync(Guid chatId, bool value, CancellationToken ct = default) =>
        await EnsureAsync(await httpClient.PutAsJsonAsync($"api/chats/{chatId}/archive", new ChatFlagRequest(value), ct));

    public async Task MarkReadAsync(Guid chatId, long sequence, CancellationToken ct = default) =>
        await EnsureAsync(await httpClient.PutAsJsonAsync($"api/chats/{chatId}/read", new ReadChatRequest(sequence), ct));

    public async Task DeleteChatAsync(Guid chatId, CancellationToken ct = default) =>
        await EnsureAsync(await httpClient.DeleteAsync($"api/chats/{chatId}", ct));

    public Task<ChatResponse> CreateDirectAsync(Guid userId, CancellationToken ct = default) =>
        PostAsync<ChatResponse>("api/chats/direct", new CreateDirectChatRequest(userId), ct);

    public Task<ChatResponse> CreateGroupAsync(string title, IReadOnlyList<Guid> userIds, CancellationToken ct = default) =>
        PostAsync<ChatResponse>("api/chats/groups", new CreateGroupChatRequest(title, userIds), ct);

    public Task<MessageResponse> SendAsync(Guid chatId, Guid clientId, string body, CancellationToken ct = default) =>
        PostAsync<MessageResponse>($"api/chats/{chatId}/messages", new SendMessageRequest(clientId, "text", body), ct);

    public Task<MessageResponse> SendWithAttachmentsAsync(Guid chatId, Guid clientId, string type, string? body, IReadOnlyList<Guid> objectIds, CancellationToken ct = default) =>
        PostAsync<MessageResponse>($"api/chats/{chatId}/messages", new SendMessageRequest(clientId, type, body, objectIds), ct);

    public async Task<IReadOnlyList<MessageResponse>> SearchAsync(Guid chatId, string query, CancellationToken ct = default) =>
        (await httpClient.GetFromJsonAsync<MessagePageResponse>(
            $"api/chats/{chatId}/messages/search?query={Uri.EscapeDataString(query)}", ct))?.Items ?? [];

    public async Task<IReadOnlyList<MessageResponse>> GetPinsAsync(Guid chatId, CancellationToken ct = default) =>
        (await httpClient.GetFromJsonAsync<MessagePageResponse>($"api/chats/{chatId}/pins", ct))?.Items ?? [];

    public async Task<IReadOnlyList<StoredObjectResponse>> GetChatMediaAsync(Guid chatId, CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<List<StoredObjectResponse>>($"api/chats/{chatId}/media", ct) ?? [];

    public async Task<ContactResponse> UpdateContactAsync(
        Guid contactId,
        UpdateContactRequest request,
        CancellationToken ct = default)
    {
        using var response = await httpClient.PatchAsJsonAsync($"api/contacts/{contactId}", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ContactResponse>(ct)
            ?? throw new InvalidDataException("Missing contact response.");
    }

    private async Task<T> PostAsync<T>(string uri, object request, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync(uri, request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct)
            ?? throw new InvalidDataException($"Missing {typeof(T).Name} response.");
    }

    private static Task EnsureAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        response.Dispose();
        return Task.CompletedTask;
    }
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public sealed class PhoneDialer : IPhoneDialer
{
    public Task OpenAsync(string uri) => Launcher.Default.OpenAsync(uri);
}
#endif
