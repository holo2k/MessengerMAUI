using Messenger.Contracts.Chats;
using Messenger.Contracts.Contacts;
using Messenger.Contracts.Media;
using Messenger.Contracts.Messages;
using Messenger.Maui.Features.Contacts;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Contacts;

public sealed class UserDetailsViewModelTests
{
    [Fact]
    public async Task User_details_load_merges_owned_contact_alias_and_mute_state()
    {
        var userId = Guid.NewGuid();
        var api = new UserDetailsApi
        {
            User = new UserSearchResponse(userId, "Евгений", "Иванов", "evgeniy", "+79512723576", null, "Био")
        };
        api.Contacts.Add(new ContactResponse(
            Guid.NewGuid(), userId, "Евгений", "Иванов", "evgeniy", "+79512723576", null, "Био",
            "Женя", "Работа", true, DateTimeOffset.UtcNow));
        var viewModel = new UserDetailsViewModel(api, new RecordingDialer());

        await viewModel.LoadAsync(userId);

        Assert.Equal(userId, viewModel.UserId);
        Assert.Equal("Женя Работа", viewModel.DisplayName);
        Assert.True(viewModel.CanEdit);
        Assert.True(viewModel.IsMuted);
        Assert.Equal("+79512723576", viewModel.Phone);
    }

    [Fact]
    public async Task User_details_without_contact_or_visible_phone_disables_edit_and_call()
    {
        var userId = Guid.NewGuid();
        var api = new UserDetailsApi
        {
            User = new UserSearchResponse(userId, "Скрытый", "Пользователь", null, null, null, null)
        };
        var viewModel = new UserDetailsViewModel(api, new RecordingDialer());

        await viewModel.LoadAsync(userId);

        Assert.False(viewModel.CanEdit);
        Assert.False(viewModel.CanCall);
        Assert.Null(viewModel.Phone);
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.SaveAliasAsync("Имя", null, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.CallAsync());
    }

    [Fact]
    public async Task User_details_saves_only_owned_contact_id_and_dials_exact_visible_number()
    {
        var userId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var api = new UserDetailsApi
        {
            User = new UserSearchResponse(userId, "Евгений", "", null, "+79512723576", null, null)
        };
        api.Contacts.Add(new ContactResponse(
            contactId, userId, "Евгений", "", null, "+79512723576", null, null,
            null, null, false, DateTimeOffset.UtcNow));
        var dialer = new RecordingDialer();
        var viewModel = new UserDetailsViewModel(api, dialer);
        await viewModel.LoadAsync(userId);

        await viewModel.SaveAliasAsync("Женя", null, true);
        await viewModel.CallAsync();

        Assert.Equal(contactId, api.UpdatedContactId);
        Assert.Equal("Женя", api.UpdateRequest?.LocalFirstName);
        Assert.True(api.UpdateRequest?.IsMuted);
        Assert.Equal("tel:+79512723576", dialer.LastUri);
    }

    private sealed class RecordingDialer : IPhoneDialer
    {
        public string? LastUri { get; private set; }
        public Task OpenAsync(string uri) { LastUri = uri; return Task.CompletedTask; }
    }

    private sealed class UserDetailsApi : IConversationApi
    {
        public required UserSearchResponse User { get; init; }
        public List<ContactResponse> Contacts { get; } = [];
        public Guid? UpdatedContactId { get; private set; }
        public UpdateContactRequest? UpdateRequest { get; private set; }

        public Task<UserSearchResponse> GetVisibleUserAsync(Guid userId, CancellationToken ct = default)
        {
            Assert.Equal(User.UserId, userId);
            return Task.FromResult(User);
        }
        public Task<IReadOnlyList<ContactResponse>> GetContactsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ContactResponse>>(Contacts);
        public Task<ContactResponse> UpdateContactAsync(Guid contactId, UpdateContactRequest request, CancellationToken ct = default)
        {
            UpdatedContactId = contactId;
            UpdateRequest = request;
            var contact = Contacts.Single(value => value.Id == contactId) with
            {
                LocalFirstName = request.LocalFirstName,
                LocalLastName = request.LocalLastName,
                IsMuted = request.IsMuted
            };
            return Task.FromResult(contact);
        }

        public Task<IReadOnlyList<ChatResponse>> GetChatsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChatResponse>>([]);
        public Task<ChatResponse> GetChatAsync(Guid chatId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChatFolderResponse>> GetFoldersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChatFolderResponse>>([]);
        public Task<IReadOnlyList<UserSearchResponse>> SearchUsersAsync(string query, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UserSearchResponse>>([]);
        public Task<IReadOnlyList<MessageResponse>> GetMessagesAsync(Guid chatId, long afterSequence, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MessageResponse>>([]);
        public Task ReorderFoldersAsync(IReadOnlyList<Guid> folderIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetArchivedAsync(Guid chatId, bool value, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkReadAsync(Guid chatId, long sequence, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteChatAsync(Guid chatId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ChatResponse> CreateDirectAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChatResponse> CreateGroupAsync(string title, IReadOnlyList<Guid> userIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MessageResponse> SendAsync(Guid chatId, Guid clientId, string body, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MessageResponse> SendWithAttachmentsAsync(Guid chatId, Guid clientId, string type, string? body, IReadOnlyList<Guid> objectIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MessageResponse>> SearchAsync(Guid chatId, string query, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MessageResponse>>([]);
        public Task<IReadOnlyList<MessageResponse>> GetPinsAsync(Guid chatId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MessageResponse>>([]);
        public Task<IReadOnlyList<StoredObjectResponse>> GetChatMediaAsync(Guid chatId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StoredObjectResponse>>([]);
    }
}
