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
        var values = new[] { "everybody", "contacts", "nobody" }; PhonePrivacyPicker.ItemsSource = values; AvatarPrivacyPicker.ItemsSource = values; LastSeenPrivacyPicker.ItemsSource = values;
    }
    protected override async void OnAppearing() { base.OnAppearing(); await SafeAsync(_viewModel.LoadAsync); }
    private async void OnSaveProfileClicked(object? s, EventArgs e) => await SafeAsync(_viewModel.SaveProfileAsync);
    private async void OnSavePrivacyClicked(object? s, EventArgs e) => await SafeAsync(_viewModel.SavePrivacyAsync);
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
