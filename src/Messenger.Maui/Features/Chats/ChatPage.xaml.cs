using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public partial class ChatPage : ContentPage
{
    private readonly ChatViewModel _viewModel;
    private readonly ChatSyncService _sync;
    private bool _started;

    public Guid CurrentUserId { get; }

    public ChatPage(ChatViewModel viewModel, ChatSyncService sync, Guid currentUserId = default, string title = "Чат")
    {
        CurrentUserId = currentUserId;
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _sync = sync;
        ChatTitle.Text = title;
        HeaderAvatar.Person = title;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _sync.MessageReceived += OnMessageReceivedAsync;
        _sync.ConnectionStateChanged += OnConnectionStateChanged;
        await RunAsync(_viewModel.LoadAsync);
        if (!_started)
        {
            _started = true;
            await RunAsync(ct => _sync.StartAsync(_viewModel.ChatId, ct));
        }
        UpdateComposer();
    }

    protected override void OnDisappearing()
    {
        _sync.Stop();
        _sync.MessageReceived -= OnMessageReceivedAsync;
        _sync.ConnectionStateChanged -= OnConnectionStateChanged;
        _started = false;
        base.OnDisappearing();
    }

    private Task OnMessageReceivedAsync(Messenger.Contracts.Messages.MessageResponse message) =>
        MainThread.InvokeOnMainThreadAsync(() => _viewModel.Receive(message));
    private void OnConnectionStateChanged(ChatConnectionState state) =>
        MainThread.BeginInvokeOnMainThread(() => _viewModel.SetConnectionState(state));

    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();
    private void OnToggleSearchClicked(object? sender, EventArgs e)
    {
        SearchPanel.IsVisible = !SearchPanel.IsVisible;
        if (SearchPanel.IsVisible) SearchEntry.Focus();
        else MessagesList.ItemsSource = _viewModel.Messages;
    }
    private void OnResetSearchClicked(object? sender, EventArgs e)
    {
        SearchEntry.Text = string.Empty;
        MessagesList.ItemsSource = _viewModel.Messages;
    }
    private async void OnMoreClicked(object? sender, EventArgs e)
    {
        var action = await DisplayActionSheetAsync("Чат", "Отмена", null, "Закреплённые сообщения", "Медиа");
        if (action == "Закреплённые сообщения") OnPinsClicked(sender, e);
        if (action == "Медиа") OnMediaClicked(sender, e);
    }
    private async void OnAttachClicked(object? sender, EventArgs e)
    {
        var action = await DisplayActionSheetAsync("Прикрепить", "Отмена", null, "Файл", "Фото", "Видео", "Аудио");
        var kind = action switch { "Файл" => MediaKind.File, "Фото" => MediaKind.Photo, "Видео" => MediaKind.Video, "Аудио" => MediaKind.Audio, _ => (MediaKind?)null };
        if (kind.HasValue) await RunMediaAsync(kind.Value);
    }
    private void OnComposerChanged(object? sender, TextChangedEventArgs e) => UpdateComposer();
    private void UpdateComposer()
    {
        if (SendButton is null) return;
        var canSend = !string.IsNullOrWhiteSpace(ComposerEntry.Text) || _viewModel?.Media?.Attachments.Count > 0;
        SendButton.IsVisible = canSend;
        AudioButton.IsVisible = !canSend;
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        await RunAsync(_viewModel.SendAsync);
        MessagesList.ItemsSource = _viewModel.Messages;
        UpdateComposer();
    }
    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        await RunAsync(ct => _viewModel.SearchAsync(SearchEntry.Text ?? string.Empty, ct));
        EmptyMessageLabel.Text = "Сообщения не найдены";
        MessagesList.ItemsSource = _viewModel.SearchResults;
    }
    private async void OnPinsClicked(object? sender, EventArgs e)
    {
        await RunAsync(_viewModel.LoadPinsAsync);
        EmptyMessageLabel.Text = "Нет закреплённых сообщений";
        MessagesList.ItemsSource = _viewModel.Pins;
        SearchPanel.IsVisible = true;
    }
    private async void OnMediaClicked(object? sender, EventArgs e)
    {
        await RunAsync(_viewModel.LoadMediaAsync);
        await Navigation.PushAsync(new SharedMediaPage(_viewModel.SharedMedia));
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
            if (!string.IsNullOrWhiteSpace(_viewModel.Media.Status))
                await DisplayAlertAsync("Вложение", _viewModel.Media.Status, "OK");
            UpdateComposer();
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        try { await action(CancellationToken.None); }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }
}
