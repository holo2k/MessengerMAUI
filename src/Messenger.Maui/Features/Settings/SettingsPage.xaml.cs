using Messenger.Contracts.Users;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Settings;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private readonly IMediaCaptureService _capture;
    private readonly IMediaUploader _uploader;
    private readonly IAvatarClient _avatars;
    public SettingsPage(SettingsViewModel viewModel, IMediaCaptureService capture, IMediaUploader uploader, IAvatarClient avatars)
    {
        InitializeComponent(); BindingContext = _viewModel = viewModel; _capture = capture; _uploader = uploader; _avatars = avatars;
        var values = new[] { "Все", "Мои контакты", "Никто" }; PhonePrivacyPicker.ItemsSource = values; AvatarPrivacyPicker.ItemsSource = values; LastSeenPrivacyPicker.ItemsSource = values;
    }
    private static readonly string[] PrivacyValues = ["everybody", "contacts", "nobody"];
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await SafeAsync(_viewModel.LoadAsync);
        PhonePrivacyPicker.SelectedIndex = Array.IndexOf(PrivacyValues, _viewModel.PhonePrivacy);
        AvatarPrivacyPicker.SelectedIndex = Array.IndexOf(PrivacyValues, _viewModel.AvatarPrivacy);
        LastSeenPrivacyPicker.SelectedIndex = Array.IndexOf(PrivacyValues, _viewModel.LastSeenPrivacy);
    }
    private async void OnEditProfileClicked(object? s, EventArgs e)
    {
        if (ProfileDetails.IsVisible)
        {
            await SafeAsync(_viewModel.SaveProfileAsync);
            ProfileDetails.IsVisible = false;
            EditProfileButton.Text = "Ред.";
        }
        else { ProfileDetails.IsVisible = true; EditProfileButton.Text = "Готово"; }
    }
    private void OnSectionTapped(object? s, TappedEventArgs e)
    {
        var section = (e.Parameter as string) switch
        {
            "phone" => PhoneSection, "privacy" => PrivacySection, "security" => SecuritySection,
            "twoFactor" => TwoFactorSection, "support" => SupportSection, "deletion" => DeletionSection, _ => null
        };
        if (section is null) return;
        var visible = !section.IsVisible;
        foreach (var item in new[] { PhoneSection, PrivacySection, SecuritySection, TwoFactorSection, SupportSection, DeletionSection }) item.IsVisible = false;
        section.IsVisible = visible;
    }
    private async void OnSaveProfileClicked(object? s, EventArgs e) => await SafeAsync(_viewModel.SaveProfileAsync);
    private async void OnSavePrivacyClicked(object? s, EventArgs e)
    {
        _viewModel.PhonePrivacy = PrivacyValues[Math.Max(0, PhonePrivacyPicker.SelectedIndex)];
        _viewModel.AvatarPrivacy = PrivacyValues[Math.Max(0, AvatarPrivacyPicker.SelectedIndex)];
        _viewModel.LastSeenPrivacy = PrivacyValues[Math.Max(0, LastSeenPrivacyPicker.SelectedIndex)];
        await SafeAsync(_viewModel.SavePrivacyAsync);
    }
    private async void OnBeginPhoneClicked(object? s, EventArgs e) => await SafeAsync(ct => _viewModel.BeginPhoneChangeAsync(_viewModel.Phone, ct));
    private async void OnConfirmPhoneClicked(object? s, EventArgs e) { var code = await DisplayPromptAsync("Смена номера", "Код из SMS (dev: 111111)", keyboard: Keyboard.Numeric); if (!string.IsNullOrWhiteSpace(code)) await SafeAsync(ct => _viewModel.ConfirmPhoneChangeAsync(code, ct)); }
    private async void OnBeginTwoFactorClicked(object? s, EventArgs e) => await SafeAsync(ct => _viewModel.BeginTwoFactorAsync(EmailEntry.Text ?? "", ct));
    private async void OnConfirmTwoFactorClicked(object? s, EventArgs e) => await SafeAsync(ct => _viewModel.ConfirmTwoFactorAsync(EmailCodeEntry.Text ?? "", ct));
    private async void OnDisableTwoFactorClicked(object? s, EventArgs e) => await SafeAsync(_viewModel.DisableTwoFactorAsync);
    private async void OnRevokeSessionClicked(object? s, EventArgs e) { if ((s as Button)?.CommandParameter is SessionResponse item) await SafeAsync(ct => _viewModel.RevokeSessionAsync(item, ct)); }
    private async void OnSupportClicked(object? s, EventArgs e) { var subject = await DisplayPromptAsync("Поддержка", "Тема"); var body = await DisplayPromptAsync("Поддержка", "Сообщение"); if (!string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(body)) await SafeAsync(ct => _viewModel.CreateTicketAsync(subject, body, ct)); }
    private async void OnDeleteClicked(object? s, EventArgs e) { var accepted = await DisplayAlertAsync("Удалить аккаунт?", "Сессии будут завершены сразу, а профиль — обезличен через 30 дней. До этого запрос можно отменить.", "Удалить", "Отмена"); await SafeAsync(ct => _viewModel.RequestDeletionAsync(accepted, ct)); }
    private async void OnCancelDeletionClicked(object? s, EventArgs e) => await SafeAsync(_viewModel.CancelDeletionAsync);
    private async void OnAvatarClicked(object? s, EventArgs e)
    {
        await SafeAsync(async ct => { var media = await _capture.AcquireAsync(MediaKind.Photo); if (media.Selection is null) return; var stored = await _uploader.UploadAsync(media.Selection, null, ct); await _avatars.AssignProfileAsync(stored.Id, ct); });
    }
    private async Task SafeAsync(Func<CancellationToken, Task> action) { try { await action(CancellationToken.None); } catch (Exception ex) { await DisplayAlertAsync("Ошибка", ex.Message, "OK"); } }
}
