using System.Net.Http.Json;
using System.Net;
using Messenger.Contracts.Chats;
using Messenger.Contracts.Contacts;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Media;

namespace Messenger.Maui.Services;

public interface IConversationApi
{
    Task<MessageResponse> GetMessageAsync(Guid messageId, CancellationToken ct = default);
    Task<MessageResponse> EditMessageAsync(Guid messageId, string body, CancellationToken ct = default);
    Task DeleteMessageAsync(Guid messageId, bool globally, CancellationToken ct = default);
    Task<MessageResponse> ForwardMessageAsync(Guid messageId, Guid targetChatId, Guid clientId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatResponse>> GetChatsAsync(CancellationToken ct = default);
    Task<ChatResponse> GetChatAsync(Guid chatId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMemberResponse>> GetChatMembersAsync(Guid chatId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatFolderResponse>> GetFoldersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ContactResponse>> GetContactsAsync(CancellationToken ct = default);
    Task<ContactResponse?> GetContactByUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserSearchResponse>> SearchUsersAsync(string query, CancellationToken ct = default);
    Task<UserSearchResponse> GetVisibleUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid chatId, long afterSequence, CancellationToken ct = default);
    Task ReorderFoldersAsync(IReadOnlyList<Guid> folderIds, CancellationToken ct = default);
    Task SetArchivedAsync(Guid chatId, bool value, CancellationToken ct = default);
    Task MarkReadAsync(Guid chatId, long sequence, CancellationToken ct = default);
    Task DeleteChatAsync(Guid chatId, CancellationToken ct = default);
    Task<ChatResponse> CreateDirectAsync(Guid userId, CancellationToken ct = default);
    Task<ChatResponse> CreateGroupAsync(string title, IReadOnlyList<Guid> userIds, CancellationToken ct = default);
    Task<MessageResponse> SendAsync(Guid chatId, Guid clientId, string body, CancellationToken ct = default);
    Task<MessageResponse> SendWithAttachmentsAsync(Guid chatId, Guid clientId, string type, string? body, IReadOnlyList<Guid> objectIds, CancellationToken ct = default,
        long? durationMs = null, IReadOnlyList<byte>? waveform = null);
    Task<IReadOnlyList<MessageResponse>> SearchAsync(Guid chatId, string query, CancellationToken ct = default);
    Task<IReadOnlyList<MessageResponse>> GetPinsAsync(Guid chatId, CancellationToken ct = default);
    Task<IReadOnlyList<StoredObjectResponse>> GetChatMediaAsync(Guid chatId, CancellationToken ct = default);
    Task<ContactResponse> UpdateContactAsync(Guid contactId, UpdateContactRequest request, CancellationToken ct = default);
    Task<ContactResponse> AddContactAsync(Guid userId, string? localFirstName, string? localLastName, CancellationToken ct = default);
}

public interface IPhoneDialer
{
    Task OpenAsync(string uri);
}

public sealed class ConversationClient(HttpClient httpClient) : IConversationApi
{
    public async Task<MessageResponse> GetMessageAsync(Guid messageId, CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<MessageResponse>($"api/messages/{messageId}", ct)
            ?? throw new InvalidDataException("Missing message response.");

    public async Task<MessageResponse> EditMessageAsync(Guid messageId, string body, CancellationToken ct = default)
    {
        using var response = await httpClient.PatchAsJsonAsync($"api/messages/{messageId}", new EditMessageRequest(body), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MessageResponse>(ct) ?? throw new InvalidDataException("Missing message response.");
    }

    public async Task DeleteMessageAsync(Guid messageId, bool globally, CancellationToken ct = default) =>
        await EnsureAsync(await httpClient.DeleteAsync($"api/messages/{messageId}?globally={globally.ToString().ToLowerInvariant()}", ct));

    public Task<MessageResponse> ForwardMessageAsync(Guid messageId, Guid targetChatId, Guid clientId, CancellationToken ct = default) =>
        PostAsync<MessageResponse>($"api/messages/{messageId}/forward", new ForwardMessageRequest(targetChatId, clientId), ct);

    public async Task<IReadOnlyList<ChatResponse>> GetChatsAsync(CancellationToken ct = default)
    {
        var chats = new List<ChatResponse>();
        string? cursor = null;
        var cursors = new HashSet<string>();
        do
        {
            var path = "api/chats?limit=100" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await httpClient.GetFromJsonAsync<ChatPageResponse>(path, ct) ?? throw new InvalidDataException("Missing chat page.");
            chats.AddRange(page.Items);
            cursor = page.NextCursor;
            if (cursor is not null && !cursors.Add(cursor)) throw new InvalidDataException("Invalid chat cursor.");
        } while (cursor is not null);
        return chats;
    }

    public async Task<ChatResponse> GetChatAsync(Guid chatId, CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<ChatResponse>($"api/chats/{chatId}", ct)
        ?? throw new InvalidDataException("Missing chat response.");

    public async Task<IReadOnlyList<ChatMemberResponse>> GetChatMembersAsync(Guid chatId, CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<List<ChatMemberResponse>>($"api/chats/{chatId}/members", ct) ?? [];

    public async Task<IReadOnlyList<ChatFolderResponse>> GetFoldersAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<List<ChatFolderResponse>>("api/chat-folders", ct) ?? [];

    public async Task<IReadOnlyList<ContactResponse>> GetContactsAsync(CancellationToken ct = default)
    {
        var contacts = new List<ContactResponse>();
        string? cursor = null;
        var cursors = new HashSet<string>();
        do
        {
            var path = "api/contacts?limit=100" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await httpClient.GetFromJsonAsync<ContactPageResponse>(path, ct) ?? throw new InvalidDataException("Missing contacts response.");
            contacts.AddRange(page.Items);
            cursor = page.NextCursor;
            if (cursor is not null && !cursors.Add(cursor)) throw new InvalidDataException("Invalid contacts cursor.");
        } while (cursor is not null);
        return contacts;
    }

    public async Task<ContactResponse> AddContactAsync(Guid userId, string? localFirstName, string? localLastName, CancellationToken ct = default)
    {
        static string? Alias(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        using var response = await httpClient.PostAsJsonAsync("api/contacts/", new CreateContactRequest(userId, Alias(localFirstName), Alias(localLastName)), ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // A previous attempt or another device may have already added the contact.
            if (await GetContactByUserAsync(userId, ct) is { } existing) return existing;
            throw new HttpRequestException("Контакт уже добавлен. Обновите список контактов.", null, response.StatusCode);
        }
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new HttpRequestException("Пользователь больше недоступен. Повторите поиск.", null, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            throw new HttpRequestException("Нельзя добавить выбранного пользователя в контакты.", null, response.StatusCode);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ContactResponse>(ct) ?? throw new InvalidDataException("Missing contact response.");
    }

    public async Task<ContactResponse?> GetContactByUserAsync(Guid userId, CancellationToken ct = default)
    {
        string? cursor = null;
        do
        {
            var uri = "api/contacts?limit=100" + (cursor is null
                ? string.Empty
                : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await httpClient.GetFromJsonAsync<ContactPageResponse>(uri, ct)
                ?? throw new InvalidDataException("Missing contacts response.");
            var contact = page.Items.SingleOrDefault(value => value.UserId == userId);
            if (contact is not null)
            {
                return contact;
            }
            cursor = page.NextCursor;
        } while (cursor is not null);

        return null;
    }

    public async Task<IReadOnlyList<UserSearchResponse>> SearchUsersAsync(string query, CancellationToken ct = default) =>
        (await httpClient.GetFromJsonAsync<UserSearchPageResponse>(
            $"api/users/search?query={Uri.EscapeDataString(query.Trim().TrimStart('@'))}", ct))?.Items ?? [];

    public async Task<UserSearchResponse> GetVisibleUserAsync(Guid userId, CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<UserSearchResponse>($"api/users/{userId}", ct)
        ?? throw new InvalidDataException("Missing user response.");

    public async Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid chatId, long afterSequence, CancellationToken ct = default)
    {
        var messages = new List<MessageResponse>();
        var cursor = afterSequence;
        while (true)
        {
            var page = await httpClient.GetFromJsonAsync<MessagePageResponse>(
                $"api/chats/{chatId}/messages?cursor={cursor}&limit=99", ct)
                ?? throw new InvalidDataException("Missing message page.");
            messages.AddRange(page.Items);
            if (page.NextCursor is null) return messages;
            if (!long.TryParse(page.NextCursor, out var next) || next <= cursor)
                throw new InvalidDataException("Invalid message cursor.");
            cursor = next;
        }
    }

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

    public Task<MessageResponse> SendWithAttachmentsAsync(Guid chatId, Guid clientId, string type, string? body, IReadOnlyList<Guid> objectIds, CancellationToken ct = default,
        long? durationMs = null, IReadOnlyList<byte>? waveform = null) =>
        PostAsync<MessageResponse>($"api/chats/{chatId}/messages", new SendMessageRequest(clientId, type, body, objectIds, DurationMs: durationMs, Waveform: waveform), ct);

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
