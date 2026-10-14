using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Contacts;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Contacts;

public sealed class UserDetailsViewModel(
    IConversationApi api,
    Messenger.Maui.Services.IPhoneDialer dialer) : ObservableObject
{
    private UserSearchResponse? _user;
    private ContactResponse? _contact;

    public Guid UserId => _user?.UserId ?? Guid.Empty;
    public UserSearchResponse? User => _user;
    public ContactResponse? Contact => _contact;
    public string DisplayName => Join(
        _contact?.LocalFirstName ?? _user?.FirstName,
        _contact?.LocalLastName ?? _user?.LastName);
    public string? Phone => _user?.Phone;
    public string? Bio => _user?.Bio;
    public bool CanEdit => _contact is not null;
    public bool CanCall => !string.IsNullOrWhiteSpace(Phone);
    public bool IsMuted => _contact?.IsMuted ?? false;

    public async Task LoadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userTask = api.GetVisibleUserAsync(userId, cancellationToken);
        var contactsTask = api.GetContactsAsync(cancellationToken);
        await Task.WhenAll(userTask, contactsTask);
        _user = await userTask;
        _contact = (await contactsTask).SingleOrDefault(value => value.UserId == userId);
        NotifyAll();
    }

    public async Task SaveAliasAsync(
        string? localFirstName,
        string? localLastName,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        if (_contact is null)
        {
            throw new InvalidOperationException("Сначала добавьте пользователя в контакты.");
        }

        _contact = await api.UpdateContactAsync(
            _contact.Id,
            new UpdateContactRequest(localFirstName, localLastName, muted),
            cancellationToken);
        NotifyAll();
    }

    public Task CallAsync()
    {
        if (string.IsNullOrWhiteSpace(Phone))
        {
            throw new InvalidOperationException("Номер телефона недоступен из-за настроек приватности.");
        }

        return dialer.OpenAsync($"tel:{Phone}");
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(UserId));
        OnPropertyChanged(nameof(User));
        OnPropertyChanged(nameof(Contact));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Phone));
        OnPropertyChanged(nameof(Bio));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanCall));
        OnPropertyChanged(nameof(IsMuted));
    }

    private static string Join(string? firstName, string? lastName) =>
        $"{firstName} {lastName}".Trim();
}
