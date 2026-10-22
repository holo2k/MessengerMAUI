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
        _contact?.LocalFirstName,
        _contact?.LocalLastName) ?? Join(_user?.FirstName, _user?.LastName)
        ?? _user?.Username
        ?? _user?.Phone
        ?? "Пользователь";
    public string? Phone => _user?.Phone;
    public string? Bio => _user?.Bio;
    public string? AvatarObjectId => _user?.AvatarObjectId;
    public bool CanEdit => _contact is not null;
    public bool CanAdd => _user is not null && _contact is null;
    public bool CanCall => !string.IsNullOrWhiteSpace(Phone);
    public bool IsMuted => _contact?.IsMuted ?? false;

    public async Task LoadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userTask = api.GetVisibleUserAsync(userId, cancellationToken);
        var contactTask = api.GetContactByUserAsync(userId, cancellationToken);
        await Task.WhenAll(userTask, contactTask);
        _user = await userTask;
        _contact = await contactTask;
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
        OnPropertyChanged(nameof(AvatarObjectId));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanCall));
        OnPropertyChanged(nameof(IsMuted));
    }

    private static string? Join(string? firstName, string? lastName)
    {
        var value = $"{firstName} {lastName}".Trim();
        return value.Length == 0 ? null : value;
    }
}
