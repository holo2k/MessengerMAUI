using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Support;
using Messenger.Contracts.Users;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Settings;

public sealed class SettingsViewModel(ISettingsApi api) : ObservableObject
{
    private string _firstName = "", _lastName = "", _username = "", _bio = "", _phone = "", _phonePrivacy = "contacts", _avatarPrivacy = "everybody", _lastSeenPrivacy = "contacts", _status = "";
    private Guid? _twoFactorChallengeId, _phoneChallengeId;
    private bool _deletionScheduled;
    public ObservableCollection<SessionResponse> Sessions { get; } = [];
    public ObservableCollection<SupportTicketResponse> Tickets { get; } = [];
    public string FirstName { get => _firstName; set => SetProperty(ref _firstName, value); }
    public string LastName { get => _lastName; set => SetProperty(ref _lastName, value); }
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string Bio { get => _bio; set => SetProperty(ref _bio, value); }
    public string Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string PhonePrivacy { get => _phonePrivacy; set => SetProperty(ref _phonePrivacy, value); }
    public string AvatarPrivacy { get => _avatarPrivacy; set => SetProperty(ref _avatarPrivacy, value); }
    public string LastSeenPrivacy { get => _lastSeenPrivacy; set => SetProperty(ref _lastSeenPrivacy, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool DeletionScheduled { get => _deletionScheduled; private set => SetProperty(ref _deletionScheduled, value); }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var me = await api.GetMeAsync(ct); FirstName = me.FirstName; LastName = me.LastName; Username = me.Username ?? ""; Bio = me.Bio ?? ""; Phone = me.Phone ?? "";
        var privacy = await api.GetPrivacyAsync(ct); PhonePrivacy = privacy.Phone; AvatarPrivacy = privacy.Avatar; LastSeenPrivacy = privacy.LastSeen;
        Sessions.Clear(); foreach (var item in await api.GetSessionsAsync(ct)) Sessions.Add(item);
        Tickets.Clear(); foreach (var item in await api.ListTicketsAsync(ct)) Tickets.Add(item);
    }
    public async Task SaveProfileAsync(CancellationToken ct = default) { await api.UpdateMeAsync(new(FirstName, LastName, NullIfBlank(Username), NullIfBlank(Bio)), ct); Status = "Профиль сохранён"; }
    public async Task SavePrivacyAsync(CancellationToken ct = default) { await api.UpdatePrivacyAsync(new(PhonePrivacy, AvatarPrivacy, LastSeenPrivacy), ct); Status = "Приватность сохранена"; }
    public async Task RevokeSessionAsync(SessionResponse session, CancellationToken ct = default) { await api.RevokeSessionAsync(session.Id, ct); Sessions.Remove(session); Status = "Сессия завершена"; }
    public async Task BeginTwoFactorAsync(string email, CancellationToken ct = default) { var value = await api.BeginTwoFactorAsync(email, ct); _twoFactorChallengeId = value.ChallengeId; Status = "Код отправлен на почту"; }
    public async Task ConfirmTwoFactorAsync(string code, CancellationToken ct = default) { if (!_twoFactorChallengeId.HasValue) throw new InvalidOperationException("Сначала запросите код."); await api.ConfirmTwoFactorAsync(_twoFactorChallengeId.Value, code, ct); Status = "Двухфакторная защита включена"; }
    public async Task DisableTwoFactorAsync(CancellationToken ct = default) { await api.DisableTwoFactorAsync(ct); Status = "Двухфакторная защита отключена"; }
    public async Task BeginPhoneChangeAsync(string phone, CancellationToken ct = default) { var value = await api.BeginPhoneChangeAsync(phone, ct); _phoneChallengeId = value.ChallengeId; Phone = phone; Status = "SMS-код создан (в dev: 111111)"; }
    public async Task ConfirmPhoneChangeAsync(string code, CancellationToken ct = default) { if (!_phoneChallengeId.HasValue) throw new InvalidOperationException("Сначала запросите смену номера."); await api.ConfirmPhoneChangeAsync(_phoneChallengeId.Value, code, ct); Status = "Номер изменён"; }
    public async Task CreateTicketAsync(string subject, string body, CancellationToken ct = default) { var value = await api.CreateTicketAsync(subject, body, ct); Tickets.Insert(0, value.Ticket); Status = "Обращение отправлено"; }
    public async Task RequestDeletionAsync(bool warningAccepted, CancellationToken ct = default) { if (!warningAccepted) return; var value = await api.RequestDeletionAsync(ct); DeletionScheduled = true; Status = $"Аккаунт будет удалён через 30 дней: {value.ExecuteAt:d}"; }
    public async Task CancelDeletionAsync(CancellationToken ct = default) { await api.CancelDeletionAsync(ct); DeletionScheduled = false; Status = "Удаление аккаунта отменено"; }
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
