using Microsoft.Extensions.DependencyInjection;

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
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RunAsync(_viewModel.InitializeAsync);
    }

    private async void OnRequestCodeClicked(object? sender, EventArgs e) =>
        await RunAsync(_viewModel.RequestCodeAsync, "Код отправлен. Для разработки: 111111.");

    private async void OnLoginClicked(object? sender, EventArgs e) =>
        await RunAsync(_viewModel.LoginAsync);

    private async void OnConfirmTwoFactorClicked(object? sender, EventArgs e) =>
        await RunAsync(_viewModel.ConfirmTwoFactorAsync);

    private async void OnOpenRegistrationClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(_services.GetRequiredService<RegistrationPage>());

    private async Task RunAsync(Func<CancellationToken, Task> action, string? success = null)
    {
        try
        {
            StatusLabel.Text = string.Empty;
            await action(CancellationToken.None);
            StatusLabel.Text = success ?? string.Empty;
        }
        catch (Exception exception)
        {
            StatusLabel.Text = exception.Message;
        }
    }
}
