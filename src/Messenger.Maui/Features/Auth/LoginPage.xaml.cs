using Microsoft.Extensions.DependencyInjection;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Auth;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;
    private readonly IServiceProvider _services;
    private bool _initialized;

    public LoginPage(LoginViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;
        await RunAsync(_viewModel.InitializeAsync);
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        if (!CodePanel.IsVisible)
        {
            await RequestCodeAsync();
            return;
        }
        var success = await RunAsync(_viewModel.RequiresTwoFactor ? _viewModel.ConfirmTwoFactorAsync : _viewModel.LoginAsync);
        if (success && _viewModel.RequiresTwoFactor)
        {
            HeaderTitle.Text = "Проверка email";
            CodeHint.Text = "Введите код из письма для подтверждения входа.";
            CodeEntry.Placeholder = "Код из email";
            _viewModel.Code = string.Empty;
            CodeEntry.Focus();
        }
    }

    private async void OnRequestCodeClicked(object? sender, EventArgs e) => await RequestCodeAsync();

    private async Task RequestCodeAsync()
    {
        if (!await RunAsync(_viewModel.RequestCodeAsync)) return;
        PhonePanel.IsVisible = false;
        CodePanel.IsVisible = true;
        BackButton.Text = "Назад";
        BackButton.IsVisible = true;
        HeaderTitle.Text = "Код подтверждения";
        CodeHint.Text = $"Код отправлен на +7 {_viewModel.Phone}";
        CodeEntry.Focus();
    }

    private void OnBackClicked(object? sender, EventArgs e)
    {
        PhonePanel.IsVisible = true;
        CodePanel.IsVisible = false;
        BackButton.IsVisible = false;
        HeaderTitle.Text = "Номер телефона";
        StatusLabel.Text = string.Empty;
    }

    private async void OnOpenRegistrationClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(_services.GetRequiredService<RegistrationPage>());

    private async Task<bool> RunAsync(Func<CancellationToken, Task> action)
    {
        if (!ContinueButton.IsEnabled) return false;
        try
        {
            ContinueButton.IsEnabled = false;
            StatusLabel.Text = string.Empty;
            await action(CancellationToken.None);
            return true;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            StatusLabel.Text = ExceptionDisplay.Format(exception);
            return false;
        }
        finally { ContinueButton.IsEnabled = true; }
    }
}
