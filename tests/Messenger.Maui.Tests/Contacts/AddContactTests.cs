using System.Net;
using System.Net.Http.Json;
using Messenger.Contracts.Contacts;
using Messenger.Maui.Features.Contacts;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Contacts;

public sealed class AddContactTests
{
    [Theory]
    [InlineData("  @alice  ", "alice")]
    [InlineData(" +79990000001 ", "+79990000001")]
    public async Task Finding_a_user_selects_their_id_and_saves_owner_local_names(string query, string expectedQuery)
    {
        using var fixture = new Fixture();
        var vm = new AddContactViewModel(fixture.Api) { Query = query };
        Assert.False(vm.CanSave);

        await vm.SearchAsync();
        Assert.Equal(expectedQuery, fixture.LastSearchQuery);
        Assert.Equal(fixture.User.UserId, vm.SelectedUser!.UserId);
        Assert.Equal("Alice", vm.FirstName);
        vm.FirstName = "  Алиса  ";
        vm.LastName = "  Работа  ";
        var contact = await vm.SaveAsync();

        Assert.Equal(fixture.User.UserId, contact!.UserId);
        var request = Assert.Single(fixture.Creates);
        Assert.Equal(fixture.User.UserId, request.TargetUserId);
        Assert.Equal("Алиса", request.LocalFirstName);
        Assert.Equal("Работа", request.LocalLastName);
        var list = new ContactsViewModel(fixture.Api, new Dialer());
        await list.LoadAsync();
        Assert.Equal(contact.Id, Assert.Single(list.Contacts).Id);
    }

    [Fact]
    public async Task Changing_the_lookup_clears_selection_and_cannot_save_the_previous_user()
    {
        using var fixture = new Fixture();
        var vm = new AddContactViewModel(fixture.Api) { Query = "alice" };
        await vm.SearchAsync();
        vm.Query = "unknown_user";
        Assert.Null(vm.SelectedUser);
        Assert.False(vm.CanSave);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveAsync());
        fixture.Found = false;
        await vm.SearchAsync();
        Assert.True(vm.ShowResults);
        Assert.Empty(vm.SearchResults);
        Assert.False(vm.CanSave);
        Assert.Empty(fixture.Creates);
    }

    [Fact]
    public async Task A_delayed_search_cannot_select_a_user_after_the_query_changed()
    {
        using var fixture = new Fixture();
        fixture.SearchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ReleaseSearch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new AddContactViewModel(fixture.Api) { Query = "alice" };
        var search = vm.SearchAsync();
        await fixture.SearchStarted.Task;
        vm.Query = "another_user";
        fixture.ReleaseSearch.SetResult();
        await search;
        Assert.Null(vm.SelectedUser);
        Assert.Empty(vm.SearchResults);
        Assert.False(vm.IsBusy);
        Assert.False(vm.CanSave);
    }

    [Fact]
    public async Task A_failed_save_preserves_the_selected_user_and_names_and_a_second_tap_does_not_duplicate_the_request()
    {
        using var fixture = new Fixture();
        fixture.SaveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ReleaseSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.CreateStatus = HttpStatusCode.ServiceUnavailable;
        var vm = new AddContactViewModel(fixture.Api);
        vm.SelectUser(fixture.User);
        vm.FirstName = "Мой контакт";
        var save = vm.SaveAsync();
        await fixture.SaveStarted.Task;
        Assert.True(vm.IsSaving);
        Assert.False(vm.CanSave);
        Assert.Null(await vm.SaveAsync());
        fixture.ReleaseSave.SetResult();
        await Assert.ThrowsAsync<HttpRequestException>(() => save);
        Assert.Single(fixture.Creates);
        Assert.Equal("Мой контакт", vm.FirstName);
        Assert.Equal(fixture.User, vm.SelectedUser);
        Assert.True(vm.CanSave);
        fixture.CreateStatus = HttpStatusCode.Created;
        Assert.NotNull(await vm.SaveAsync());
        Assert.Equal(2, fixture.Creates.Count);
    }

    [Fact]
    public async Task A_duplicate_returns_the_existing_contact_and_keeps_its_alias_even_on_a_later_page()
    {
        using var fixture = new Fixture();
        var existing = new ContactResponse(Guid.NewGuid(), fixture.User.UserId, "Alice", "", "alice", null, null, null,
            "Ранее сохранённое", "Имя", true, DateTimeOffset.UtcNow);
        fixture.Contact = existing;
        fixture.ContactOnSecondPage = true;
        fixture.CreateStatus = HttpStatusCode.Conflict;
        var vm = new AddContactViewModel(fixture.Api);
        vm.SelectUser(fixture.User);
        vm.FirstName = "Другое имя";

        Assert.Equal(existing, await vm.SaveAsync());
        Assert.Equal(existing, fixture.Contact);
        var contacts = await fixture.Api.GetContactsAsync();
        Assert.Equal(2, contacts.Count);
        Assert.Contains(contacts, contact => contact.Id == existing.Id);
    }

    private sealed class Dialer : IPhoneDialer
    {
        public Task OpenAsync(string uri) => throw new NotSupportedException();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        public IConversationApi Api { get; }
        public UserSearchResponse User { get; } = new(Guid.NewGuid(), "Alice", "A", "alice", null, null, null);
        public ContactResponse? Contact { get; set; }
        public bool Found { get; set; } = true;
        public bool ContactOnSecondPage { get; set; }
        public HttpStatusCode CreateStatus { get; set; } = HttpStatusCode.Created;
        public string? LastSearchQuery { get; private set; }
        public List<CreateContactRequest> Creates { get; } = [];
        public TaskCompletionSource? SearchStarted { get; set; }
        public TaskCompletionSource? ReleaseSearch { get; set; }
        public TaskCompletionSource? SaveStarted { get; set; }
        public TaskCompletionSource? ReleaseSave { get; set; }

        public Fixture()
        {
            _http = new HttpClient(new Handler(async (request, ct) =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/users/search")
                {
                    LastSearchQuery = Uri.UnescapeDataString(request.RequestUri.Query[7..]);
                    SearchStarted?.TrySetResult();
                    if (ReleaseSearch is not null) await ReleaseSearch.Task.WaitAsync(ct);
                    return Ok(new UserSearchPageResponse(Found ? [User] : []));
                }
                if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/api/contacts/")
                {
                    var value = (await request.Content!.ReadFromJsonAsync<CreateContactRequest>(ct))!;
                    Creates.Add(value);
                    SaveStarted?.TrySetResult();
                    if (ReleaseSave is not null) await ReleaseSave.Task.WaitAsync(ct);
                    if (CreateStatus != HttpStatusCode.Created) return new(CreateStatus);
                    Contact = new(Guid.NewGuid(), User.UserId, User.FirstName, User.LastName, User.Username, User.Phone, User.AvatarObjectId, User.Bio,
                        value.LocalFirstName, value.LocalLastName, false, DateTimeOffset.UtcNow);
                    return new(HttpStatusCode.Created) { Content = JsonContent.Create(Contact) };
                }
                if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/api/contacts")
                {
                    if (ContactOnSecondPage && !request.RequestUri.Query.Contains("cursor="))
                    {
                        var other = new ContactResponse(Guid.NewGuid(), Guid.NewGuid(), "Other", "", null, null, null, null, null, null, false, DateTimeOffset.UtcNow);
                        return Ok(new ContactPageResponse([other], "next/page+cursor="));
                    }
                    return Ok(new ContactPageResponse(Contact is { } contact ? [contact] : [], null));
                }
                throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
            })) { BaseAddress = new Uri("https://api.test/") };
            Api = new ConversationClient(_http);
        }

        private static HttpResponseMessage Ok<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        public void Dispose() => _http.Dispose();
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
