namespace Messenger.Maui.Features.Auth;

public partial class RegistrationPage : ContentPage
{
    private readonly RegistrationViewModel _viewModel;

    public RegistrationPage(RegistrationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    private async void OnRequestCodeClicked(object? sender, EventArgs e) =>
        await RunAsync(_viewModel.RequestCodeAsync, "Код отправлен. Для разработки: 111111.");

    private async void OnRegisterClicked(object? sender, EventArgs e) =>
        await RunAsync(_viewModel.RegisterAsync);

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
