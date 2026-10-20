using Messenger.Contracts.Chats;
using Messenger.Maui.Services;
using System.ComponentModel;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Realtime;

namespace Messenger.Maui.Features.Chats;

public partial class ChatPage : ContentPage
{
    private readonly ChatViewModel _viewModel;
    private readonly ChatSyncService _sync;
    private readonly ChatResponse _chat;
    private readonly Func<Guid, Task>? _openProfile;
    private bool _isActive, _windowActive;
    private int _lastVisibleIndex = -1;
    private Window? _window;

    public Guid CurrentUserId { get; }
    public long ReadSequence => _viewModel?.ReadSequence ?? 0;

    public ChatPage(
        ChatViewModel viewModel,
        ChatSyncService sync,
        ChatResponse chat,
        Guid currentUserId = default,
        Func<Guid, Task>? openProfile = null)
    {
        CurrentUserId = currentUserId;
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.SetCurrentUser(currentUserId);
        MessagesList.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ItemsView.ItemsSource)) _lastVisibleIndex = -1;
        };
        _sync = sync;
        _chat = chat;
        _openProfile = openProfile;
        var title = chat.Type == "direct"
            ? chat.Peer?.DisplayName ?? "Пользователь"
            : string.IsNullOrWhiteSpace(chat.Title) ? "Групповой чат" : chat.Title;
        ChatTitle.Text = title;
        HeaderAvatar.Person = title;
        HeaderAvatar.AvatarObjectId = chat.Type == "direct"
            ? chat.Peer?.AvatarObjectId
            : chat.AvatarObjectId;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_isActive) return;
        _isActive = _windowActive = true;
        _window = Window;
        if (_window is not null)
        {
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
            _window.Stopped += OnWindowDeactivated;
        }
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _sync.MessageReceived += OnMessageReceivedAsync;
        _sync.ReadPositionChanged += OnReadPositionChangedAsync;
        _sync.ReadPositionsLoaded += OnReadPositionsLoadedAsync;
        _sync.ConnectionStateChanged += OnConnectionStateChanged;
        await RunAsync(_viewModel.LoadAsync);
        if (!_isActive) return;
        await RunAsync(ct => _sync.StartAsync(_viewModel.ChatId, ct));
        if (!_isActive) { _sync.Stop(); return; }
        UpdateComposer();
        Dispatcher.Dispatch(ScrollToLatestMessage);
    }

    protected override void OnDisappearing()
    {
        _isActive = _windowActive = false;
        _lastVisibleIndex = -1;
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
            _window.Stopped -= OnWindowDeactivated;
            _window = null;
        }
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _sync.Stop();
        _sync.MessageReceived -= OnMessageReceivedAsync;
        _sync.ReadPositionChanged -= OnReadPositionChangedAsync;
        _sync.ReadPositionsLoaded -= OnReadPositionsLoadedAsync;
        _sync.ConnectionStateChanged -= OnConnectionStateChanged;
        base.OnDisappearing();
    }

    private Task OnMessageReceivedAsync(Messenger.Contracts.Messages.MessageResponse message) =>
        MainThread.InvokeOnMainThreadAsync(() => _viewModel.Receive(message));
    private Task OnReadPositionChangedAsync(ReadPositionChangedEvent position) =>
        MainThread.InvokeOnMainThreadAsync(() => _viewModel.ApplyReadPosition(position));
    private Task OnReadPositionsLoadedAsync(IReadOnlyList<ChatMemberResponse> members) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            _viewModel.ApplyReadPositions(members);
            await MarkVisibleMessagesReadAsync();
        });
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.ReadSequence)) OnPropertyChanged(nameof(ReadSequence));
    }
    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        _windowActive = true;
        await MarkVisibleMessagesReadAsync();
    }
    private void OnWindowDeactivated(object? sender, EventArgs e) => _windowActive = false;
    private void OnMessagesLoaded(object? sender, EventArgs e) => Dispatcher.Dispatch(ScrollToLatestMessage);
    private void ScrollToLatestMessage()
    {
        if (_isActive && _viewModel.Messages.LastOrDefault() is { } last)
            MessagesList.ScrollTo(last, position: ScrollToPosition.End, animate: false);
    }
    private async void OnMessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        _lastVisibleIndex = e.LastVisibleItemIndex;
        await MarkVisibleMessagesReadAsync();
    }
    private async Task MarkVisibleMessagesReadAsync()
    {
        if (!_isActive || !_windowActive || _lastVisibleIndex < 0 ||
            MessagesList.ItemsSource is not IList<MessageResponse> messages || _lastVisibleIndex >= messages.Count) return;
        try { await _viewModel.MarkVisibleReadAsync(messages[_lastVisibleIndex].Sequence); }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine($"Read receipt: {exception.Message}"); }
    }
    private void OnConnectionStateChanged(ChatConnectionState state) =>
        MainThread.BeginInvokeOnMainThread(() => _viewModel.SetConnectionState(state));

    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();
    private async void OnHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (_chat.Type == "direct" && _chat.Peer is { } peer && _openProfile is not null)
        {
            await RunAsync(_ => _openProfile(peer.UserId));
        }
    }
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
