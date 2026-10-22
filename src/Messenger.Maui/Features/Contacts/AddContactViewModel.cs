using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Contacts;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Contacts;

public sealed class AddContactViewModel(IConversationApi api) : ObservableObject
{
    private string _query = string.Empty;
    private string _firstName = string.Empty, _lastName = string.Empty;
    private UserSearchResponse? _selectedUser;
    private bool _isSearching, _isSaving, _hasSearched;
    public ObservableCollection<UserSearchResponse> SearchResults { get; } = [];
    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value)) return;
            _selectedUser = null;
            _hasSearched = false;
            SearchResults.Clear();
            NotifyState();
        }
    }
    public string FirstName { get => _firstName; set => SetProperty(ref _firstName, value); }
    public string LastName { get => _lastName; set => SetProperty(ref _lastName, value); }
    public UserSearchResponse? SelectedUser => _selectedUser;
    public bool HasSelectedUser => _selectedUser is not null;
    public bool IsBusy => _isSearching || _isSaving;
    public bool IsSaving => _isSaving;
    public bool CanSearch => !IsBusy && !string.IsNullOrWhiteSpace(Query);
    public bool CanSave => HasSelectedUser && !IsBusy;
    public bool ShowResults => _isSearching || _hasSearched && !HasSelectedUser;

    public async Task SearchAsync(CancellationToken ct = default)
    {
        if (!CanSearch) return;
        var query = Query.Trim();
        _isSearching = true;
        _selectedUser = null;
        _hasSearched = false;
        NotifyState();
        try
        {
            var users = await api.SearchUsersAsync(query, ct);
            ct.ThrowIfCancellationRequested();
            if (Query.Trim() != query) return;
            SearchResults.Clear();
            foreach (var user in users) SearchResults.Add(user);
            _hasSearched = true;
            if (users.Count == 1) SelectCore(users[0]);
        }
        finally { _isSearching = false; NotifyState(); }
    }

    public void SelectUser(UserSearchResponse user)
    {
        if (IsBusy) return;
        SelectCore(user);
        NotifyState();
    }

    private void SelectCore(UserSearchResponse user)
    {
        _selectedUser = user;
        FirstName = user.FirstName;
        LastName = user.LastName;
    }

    public async Task<ContactResponse?> SaveAsync(CancellationToken ct = default)
    {
        if (IsBusy) return null;
        var user = _selectedUser ?? throw new InvalidOperationException("Сначала найдите и выберите пользователя.");
        _isSaving = true;
        NotifyState();
        try { return await api.AddContactAsync(user.UserId, FirstName, LastName, ct); }
        finally { _isSaving = false; NotifyState(); }
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(SelectedUser));
        OnPropertyChanged(nameof(HasSelectedUser));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsSaving));
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ShowResults));
    }
}
