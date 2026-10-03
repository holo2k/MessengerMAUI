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
    private async void OnMediaClicked(object? sender, EventArgs e)
    {
        await RunAsync(_viewModel.LoadMediaAsync);
        await DisplayAlertAsync("Медиа", $"Файлов в чате: {_viewModel.SharedMedia.Count}", "OK");
    }
    private async void OnFileClicked(object? sender, EventArgs e) => await RunMediaAsync(MediaKind.File);
    private async void OnPhotoClicked(object? sender, EventArgs e) => await RunMediaAsync(MediaKind.Photo);
    private async void OnVideoClicked(object? sender, EventArgs e) => await RunMediaAsync(MediaKind.Video);
    private async void OnAudioClicked(object? sender, EventArgs e) => await RunMediaAsync(MediaKind.Audio);

    private async Task RunMediaAsync(MediaKind kind)
    {
        if (_viewModel.Media is not null)
        {
            await RunAsync(ct => _viewModel.Media.AddAsync(kind, ct));
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        try { await action(CancellationToken.None); }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }
}
