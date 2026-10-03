using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public partial class ChatPage : ContentPage
{
    private readonly ChatViewModel _viewModel;
    private readonly ChatSyncService _sync;
    private bool _started;

    public ChatPage(ChatViewModel viewModel, ChatSyncService sync)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _sync = sync;
        _sync.MessageReceived += message => MainThread.InvokeOnMainThreadAsync(() => _viewModel.Receive(message));
        _sync.ConnectionStateChanged += state => MainThread.BeginInvokeOnMainThread(() => _viewModel.SetConnectionState(state));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunAsync(_viewModel.LoadAsync);
        if (!_started)
        {
            _started = true;
            await RunAsync(ct => _sync.StartAsync(_viewModel.ChatId, ct));
        }
    }

    private async void OnSendClicked(object? sender, EventArgs e) => await RunAsync(_viewModel.SendAsync);
    private async void OnSearchClicked(object? sender, EventArgs e) =>
        await RunAsync(ct => _viewModel.SearchAsync(SearchEntry.Text ?? string.Empty, ct));
    private async void OnPinsClicked(object? sender, EventArgs e) => await RunAsync(_viewModel.LoadPinsAsync);

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        try { await action(CancellationToken.None); }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }
}
