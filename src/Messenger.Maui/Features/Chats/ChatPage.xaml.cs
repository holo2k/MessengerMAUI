using Messenger.Contracts.Chats;
using Messenger.Maui.Services;
using System.ComponentModel;
using Messenger.Contracts.Messages;
using Messenger.Contracts.Realtime;
using Messenger.Contracts.Music;
using Messenger.Maui.Features.Music;
using Microsoft.Extensions.DependencyInjection;
using Messenger.Maui.Controls;

namespace Messenger.Maui.Features.Chats;

public partial class ChatPage : ContentPage
{
    private readonly ChatViewModel _viewModel;
    private readonly ChatSyncService _sync;
    private readonly ChatResponse _chat;
    private readonly Func<Guid, Task>? _openProfile;
    private bool _isActive, _windowActive;
    private bool _messageMenuOpen, _sendingMessage;
    private int _lastVisibleIndex = -1;
    private Window? _window;
    private AttachmentPickerView? _attachmentPicker;
    private CancellationTokenSource? _searchCancellation;
    public MusicViewModel? Music { get; private set; }

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
        if (viewModel.Media is not null) viewModel.Media.Attachments.CollectionChanged += (_, _) => UpdateComposer();
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
        Music = (Shell.Current as AppShell)?.Music;
        if (Music is not null)
        {
            Music.TrackUpdated += _viewModel.ApplyMusicTrack;
            if (MiniPlayerHost.Content is null)
            {
                var miniPlayer = new MiniPlayerView(Music, () => MusicPlayerPage.OpenAsync(Navigation, Music));
                miniPlayer.SetCompact(true);
                MiniPlayerHost.Content = miniPlayer;
                MiniPlayerHost.SetBinding(IsVisibleProperty, new Binding(nameof(MusicViewModel.HasCurrentTrack), source: Music));
            }
        }
        OnPropertyChanged(nameof(Music));
        _window = Window;
        if (_window is not null)
        {
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
            _window.Stopped += OnWindowDeactivated;
        }
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _sync.MessageReceived += OnMessageReceivedAsync;
        _sync.MessagesReconciled += OnMessagesReconciledAsync;
        _sync.MessageRemoved += OnMessageRemovedAsync;
        _sync.ReadPositionChanged += OnReadPositionChangedAsync;
        _sync.ReadPositionsLoaded += OnReadPositionsLoadedAsync;
        _sync.ConnectionStateChanged += OnConnectionStateChanged;
        await MessagesLoading.LoadAsync(() => RunAsync(_viewModel.LoadAsync));
        if (!_isActive) return;
        await RunAsync(ct => _sync.StartAsync(_viewModel.ChatId, ct));
        if (!_isActive) { _sync.Stop(); return; }
        UpdateComposer();
        Dispatcher.Dispatch(ScrollToLatestMessage);
    }

    protected override void OnDisappearing()
    {
        _isActive = _windowActive = false;
        _searchCancellation?.Cancel();
        CloseAttachmentPicker();
        if (Music is not null) Music.TrackUpdated -= _viewModel.ApplyMusicTrack;
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
        _sync.MessagesReconciled -= OnMessagesReconciledAsync;
        _sync.MessageRemoved -= OnMessageRemovedAsync;
        _sync.ReadPositionChanged -= OnReadPositionChangedAsync;
        _sync.ReadPositionsLoaded -= OnReadPositionsLoadedAsync;
        _sync.ConnectionStateChanged -= OnConnectionStateChanged;
        base.OnDisappearing();
    }

    private Task OnMessageReceivedAsync(Messenger.Contracts.Messages.MessageResponse message) =>
        MainThread.InvokeOnMainThreadAsync(() => _viewModel.Receive(message));
    private Task OnMessagesReconciledAsync(IReadOnlyList<MessageResponse> messages) =>
        MainThread.InvokeOnMainThreadAsync(() => _viewModel.ApplySnapshot(messages));
    private Task OnMessageRemovedAsync(Guid messageId) =>
        MainThread.InvokeOnMainThreadAsync(() => _viewModel.RemoveMessage(messageId));
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
        if (e.PropertyName == nameof(ChatViewModel.IsEditing)) UpdateComposer();
    }

    private async void OnMessageActionsRequested(object? sender, MessageResponse message)
    {
        if (_messageMenuOpen || _sendingMessage) return;
        _messageMenuOpen = true;
        try
        {
            var choices = new List<string>();
            if (_viewModel.CanEdit(message)) choices.Add("Редактировать");
            if (message.DeletedAt is null) choices.Add("Переслать");
            choices.Add("Удалить");
            var action = await DisplayActionSheetAsync("Сообщение", "Отмена", null, choices.ToArray());
            if (action == "Редактировать")
            {
                _viewModel.BeginEdit(message);
                UpdateComposer();
                ComposerEntry.Focus();
            }
            else if (action == "Переслать")
            {
                var api = Handler!.MauiContext!.Services.GetRequiredService<IConversationApi>();
                await Navigation.PushAsync(new MessageForwardPage(message, api,
                    async (target, clientId) => { await _viewModel.ForwardMessageAsync(message, target, clientId); }));
            }
            else if (action == "Удалить")
            {
                var canDeleteGlobally = message.SenderUserId == CurrentUserId && message.DeletedAt is null;
                var deletion = await DisplayActionSheetAsync("Удалить сообщение?", "Отмена",
                    canDeleteGlobally ? "Удалить для всех" : null, "Удалить у меня");
                if (deletion is "Удалить для всех" or "Удалить у меня")
                    await RunAsync(ct => _viewModel.DeleteMessageAsync(message, deletion == "Удалить для всех", ct));
            }
        }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
        finally { _messageMenuOpen = false; }
    }

    private void OnCancelEditClicked(object? sender, EventArgs e) { _viewModel.CancelEdit(); UpdateComposer(); }
    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        _windowActive = true;
        await MarkVisibleMessagesReadAsync();
        if (_attachmentPicker is { } picker) await picker.RefreshOnResumeAsync();
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
        if (!_isActive || !_windowActive || _attachmentPicker is not null || _lastVisibleIndex < 0 ||
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
        else { _searchCancellation?.Cancel(); MessagesList.ItemsSource = _viewModel.Messages; }
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
        if (_attachmentPicker is not null) { CloseAttachmentPicker(); return; }
        ComposerEntry.Unfocus();
        var gallery = Handler!.MauiContext!.Services.GetRequiredService<IGalleryService>();
        var picker = new AttachmentPickerView(gallery, Math.Max(0, MediaComposerViewModel.MaxAttachments - (_viewModel.Media?.Attachments.Count ?? 0)));
        _attachmentPicker = picker;
        picker.CloseRequested += CloseAttachmentPicker;
        picker.AttachRequested += AttachGalleryItemsAsync;
        picker.ActionRequested += OnAttachmentActionAsync;
        AttachmentPickerHost.Content = picker;
        AttachmentOverlay.IsVisible = true;
        OnAttachmentOverlaySizeChanged(this, EventArgs.Empty);
        await picker.OpenAsync();
    }

    private async Task OnAttachmentActionAsync(AttachmentPickerAction action)
    {
        CloseAttachmentPicker();
        if (action == AttachmentPickerAction.Music)
        {
            var api = Handler!.MauiContext!.Services.GetRequiredService<IMusicApi>();
            var clientIds = new Dictionary<Guid, Guid>();
            await Navigation.PushAsync(new MusicPickerPage(api, async track =>
            {
                if (!clientIds.TryGetValue(track.Id, out var clientId)) clientIds[track.Id] = clientId = Guid.NewGuid();
                _viewModel.Receive(await api.ShareAsync(track.Id, _viewModel.ChatId, clientId, CancellationToken.None));
            }));
            return;
        }
        await RunMediaAsync(MediaKind.File);
    }

    private async Task AttachGalleryItemsAsync(IReadOnlyList<MediaSelection> items)
    {
        CloseAttachmentPicker();
        if (_viewModel.Media is null) return;
        await RunAsync(ct => _viewModel.Media.AddSelectionsAsync(items, ct));
        UpdateComposer();
        if (_viewModel.Media.Status is { Length: > 0 } status) await DisplayAlertAsync("Вложение", status, "OK");
    }

    private void CloseAttachmentPicker()
    {
        if (_attachmentPicker is not { } picker) return;
        _attachmentPicker = null;
        picker.CloseRequested -= CloseAttachmentPicker;
        picker.AttachRequested -= AttachGalleryItemsAsync;
        picker.ActionRequested -= OnAttachmentActionAsync;
        picker.Close();
        AttachmentOverlay.IsVisible = false;
        AttachmentPickerHost.Content = null;
        Dispatcher.Dispatch(async () => await MarkVisibleMessagesReadAsync());
    }

    private void OnAttachmentBackdropTapped(object? sender, TappedEventArgs e) => CloseAttachmentPicker();
    private void OnAttachmentOverlaySizeChanged(object? sender, EventArgs e) =>
        AttachmentSheet.HeightRequest = Math.Min(Math.Max(340, Height * 0.7), Math.Max(0, Height - 24));

    protected override bool OnBackButtonPressed()
    {
        if (_viewModel.IsEditing) { _viewModel.CancelEdit(); UpdateComposer(); return true; }
        if (_attachmentPicker is null) return base.OnBackButtonPressed();
        CloseAttachmentPicker();
        return true;
    }

    private async void OnAttachmentTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ComposerAttachment { State: UploadItemState.Failed } item || _viewModel.Media is null) return;
        await RunAsync(ct => _viewModel.Media.RetryAsync(item, ct));
        UpdateComposer();
        if (_viewModel.Media.Status is { Length: > 0 } status) await DisplayAlertAsync("Вложение", status, "OK");
    }

    private void OnRemoveAttachmentClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ComposerAttachment item }) _viewModel.Media?.Attachments.Remove(item);
        UpdateComposer();
    }

    private async void OnMusicTrackOpened(object? sender, MusicTrackResponse track) => await RunAsync(async ct =>
    {
        if (Music is null) return;
        if (Music.CurrentTrack?.Id != track.Id) await PlayChatTrackAsync(track, ct);
        await MusicPlayerPage.OpenAsync(Navigation, Music);
    });

    private async void OnMusicPlaybackRequested(object? sender, MusicTrackResponse track) => await RunAsync(async ct =>
    {
        if (Music is null) return;
        if (Music.CurrentTrack?.Id != track.Id) await PlayChatTrackAsync(track, ct);
        else if (Music.HasPlaybackError) await Music.RetryAsync(ct);
        else Music.TogglePause();
    });

    private Task PlayChatTrackAsync(MusicTrackResponse track, CancellationToken ct) => Music!.PlayFromQueueAsync(track,
        _viewModel.Messages.Select(message => message.MusicTrack).OfType<MusicTrackResponse>(), $"Чат: {ChatTitle.Text}", ct);
    private void OnComposerChanged(object? sender, TextChangedEventArgs e) => UpdateComposer();
    private void UpdateComposer()
    {
        if (SendButton is null) return;
        var editing = _viewModel?.IsEditing == true;
        var canSend = editing || !string.IsNullOrWhiteSpace(ComposerEntry.Text) || _viewModel?.Media?.Attachments.Count > 0;
        SendButton.IsVisible = canSend;
        SendButton.IsEnabled = !_sendingMessage && (editing
            ? _viewModel!.EditingMessage!.Type != "text" || !string.IsNullOrWhiteSpace(ComposerEntry.Text)
            : _viewModel?.Media?.Attachments.All(item => item.State == UploadItemState.Uploaded) != false);
        SemanticProperties.SetDescription(SendButton, editing ? "Сохранить изменения" : "Отправить сообщение");
        AudioButton.IsVisible = !canSend;
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        if (_sendingMessage) return;
        _sendingMessage = true;
        UpdateComposer();
        try
        {
            await RunAsync(_viewModel.SendAsync);
            MessagesList.ItemsSource = _viewModel.Messages;
        }
        finally { _sendingMessage = false; UpdateComposer(); }
    }
    private async void OnSearchClicked(object? sender, EventArgs e) => await SearchMessagesAsync(false);
    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isActive && SearchPanel.IsVisible) await SearchMessagesAsync(true);
    }
    private async Task SearchMessagesAsync(bool debounce)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new();
        var ct = _searchCancellation.Token;
        var query = SearchEntry.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            await _viewModel.SearchAsync(string.Empty, ct);
            EmptyMessageLabel.Text = "Начните общение";
            MessagesList.ItemsSource = _viewModel.Messages;
            return;
        }
        try
        {
            if (debounce) await Task.Delay(300, ct);
            await _viewModel.SearchAsync(query, ct);
            ct.ThrowIfCancellationRequested();
            EmptyMessageLabel.Text = "Сообщения не найдены";
            MessagesList.ItemsSource = _viewModel.SearchResults;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { if (!ct.IsCancellationRequested) await DisplayAlertAsync("Поиск", exception.Message, "OK"); }
    }
    private async void OnPinsClicked(object? sender, EventArgs e)
    {
        await MessagesLoading.LoadAsync(async () =>
        {
            await RunAsync(_viewModel.LoadPinsAsync);
            EmptyMessageLabel.Text = "Нет закреплённых сообщений";
            MessagesList.ItemsSource = _viewModel.Pins;
            SearchPanel.IsVisible = true;
        }, replaceContent: true);
    }
    private async void OnMediaClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new SharedMediaPage(_viewModel.ChatId, Handler!.MauiContext!.Services.GetRequiredService<IConversationApi>()));
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
