namespace Messenger.Maui.Features.Auth;

public partial class RegistrationPage : ContentPage
{
    private readonly RegistrationViewModel _viewModel;

    public RegistrationPage(RegistrationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        if (CodePanel.IsVisible) await RunAsync(_viewModel.RegisterAsync);
        else await RequestCodeAsync();
    }

    private async void OnRequestCodeClicked(object? sender, EventArgs e) => await RequestCodeAsync();

    private async Task RequestCodeAsync()
    {
        if (!await RunAsync(_viewModel.RequestCodeAsync)) return;
        PhonePanel.IsVisible = false;
        CodePanel.IsVisible = true;
        HeaderTitle.Text = "Код подтверждения";
        CodeHint.Text = $"Код отправлен на +7 {_viewModel.Phone}";
        CodeEntry.Focus();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (CodePanel.IsVisible)
        {
            PhonePanel.IsVisible = true;
            CodePanel.IsVisible = false;
            HeaderTitle.Text = "Номер телефона";
            StatusLabel.Text = string.Empty;
        }
        else await Navigation.PopAsync();
    }

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
        catch (Exception exception) { StatusLabel.Text = exception.Message; return false; }
        finally { ContinueButton.IsEnabled = true; }
    }
}
