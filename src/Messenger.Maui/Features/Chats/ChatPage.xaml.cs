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
    private int _lastVisibleIndex = -1;
    private Window? _window;
    private AttachmentPickerView? _attachmentPicker;
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
        if (_attachmentPicker is null) return base.OnBackButtonPressed();
        CloseAttachmentPicker();
        return true;
    }

    private async void OnAttachmentTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ComposerAttachment { State: UploadItemState.Failed } item || _viewModel.Media is null) return;
        await RunAsync(ct => _viewModel.Media.RetryAsync(item, ct));
        UpdateComposer();
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
        var canSend = !string.IsNullOrWhiteSpace(ComposerEntry.Text) || _viewModel?.Media?.Attachments.Count > 0;
        SendButton.IsVisible = canSend;
        SendButton.IsEnabled = _viewModel?.Media?.Attachments.All(item => item.State == UploadItemState.Uploaded) != false;
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
