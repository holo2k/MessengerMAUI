namespace Messenger.Maui.Features.Contacts;

public partial class ContactsPage : ContentPage
{
    private readonly ContactsViewModel _viewModel;

    public ContactsPage(ContactsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunAsync(_viewModel.LoadAsync);
    }

    private async void OnSearchClicked(object? sender, EventArgs e) =>
        await RunAsync(ct => _viewModel.SearchAsync(SearchEntry.Text ?? string.Empty, ct));

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        try { await action(CancellationToken.None); }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }
}
