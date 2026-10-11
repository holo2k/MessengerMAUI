using Messenger.Contracts.Music;
using Messenger.Maui.Services;
using Messenger.Contracts.Realtime;

namespace Messenger.Maui.Features.Music;

public partial class MusicPage : ContentPage
{
    private readonly MusicViewModel _viewModel;
    private readonly IMusicFilePicker _musicFiles;
    private readonly IMediaUploader _uploader;
    private readonly IMusicRealtimeClient _realtime;
    private bool _isSearching, _isPlayerExpanded, _isSeeking, _isSaving;
    private readonly Microsoft.Maui.Dispatching.IDispatcherTimer _playbackTimer;
    public Guid? CurrentTrackId => _viewModel?.CurrentTrack?.Id;
    public bool IsPlayerExpanded
    {
        get => _isPlayerExpanded;
        private set
        {
            if (_isPlayerExpanded == value) return;
            _isPlayerExpanded = value; OnPropertyChanged();
            Shell.SetTabBarIsVisible(this, !value);
            if (!value) QueueOverlay.IsVisible = false;
        }
    }
    public MusicPage(MusicViewModel viewModel, IMusicFilePicker musicFiles, IMediaUploader uploader, IMusicRealtimeClient realtime)
    {
        InitializeComponent(); BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MusicViewModel.CurrentTrack)) OnPropertyChanged(nameof(CurrentTrackId)); };
        _musicFiles = musicFiles; _uploader = uploader; _realtime = realtime;
        _realtime.TrackStatusChanged += OnTrackStatusChangedAsync;
        _playbackTimer = Dispatcher.CreateTimer();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(250);
        _playbackTimer.Tick += (_, _) =>
        {
            if (!_isSeeking) _viewModel.RefreshPlayback();
            if (!_viewModel.HasCurrentTrack) IsPlayerExpanded = false;
        };
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); _viewModel.RefreshPlayback(); _playbackTimer.Start();
        await SafeAsync(_viewModel.LoadLibraryAsync); await SafeAsync(_realtime.ConnectAsync);
    }
    protected override void OnDisappearing() { _playbackTimer.Stop(); base.OnDisappearing(); }
    protected override bool OnBackButtonPressed()
    {
        if (QueueOverlay.IsVisible) { QueueOverlay.IsVisible = false; return true; }
        if (!IsPlayerExpanded) return base.OnBackButtonPressed();
        IsPlayerExpanded = false;
        return true;
    }
    private Task OnTrackStatusChangedAsync(MusicTrackStatusChangedEvent value) => value.Status is "blocked" or "deleted"
        ? MainThread.InvokeOnMainThreadAsync(() => _viewModel.OnTrackBlockedAsync(value.TrackId, value.Reason))
        : Task.CompletedTask;
    private async void OnSearchClicked(object? s, EventArgs e) => await SearchVisibleAsync();
    private async Task SearchVisibleAsync()
    {
        if (string.IsNullOrWhiteSpace(_viewModel.SearchText)) { ShowLibrary(); return; }
        var query = _viewModel.SearchText.Trim();
        await SafeAsync(_viewModel.SearchAsync);
        if (_viewModel.SearchText.Trim() != query) return;
        _isSearching = true;
        SectionTitle.Text = "Результаты поиска";
        EmptyMusicLabel.Text = "Ничего не найдено";
        TracksList.ItemsSource = _viewModel.SearchResults;
    }
    private async void OnSearchTextChanged(object? s, TextChangedEventArgs e)
    {
        if (_viewModel is null || TracksList is null) return;
        if (string.IsNullOrWhiteSpace(e.NewTextValue) && TracksList is not null)
        {
            ShowLibrary();
        }
        else await SearchVisibleAsync();
    }
    private async void OnTrackSelected(object? s, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MusicTrackResponse track) return;
        TracksList.SelectedItem = null;
        await StartSelectedTrackAsync(track);
    }
    private async void OnAddClicked(object? s, EventArgs e) { if ((s as ImageButton)?.CommandParameter is MusicTrackResponse t) await SafeAsync(ct => _viewModel.AddAsync(t, ct)); }
    private async void OnDownloadClicked(object? s, EventArgs e) { if ((s as ImageButton)?.CommandParameter is MusicTrackResponse t) await SafeAsync(ct => _viewModel.DownloadAsync(t, ct)); }
    private async void OnPauseClicked(object? s, EventArgs e)
    {
        if (_viewModel.HasPlaybackError) await _viewModel.RetryAsync();
        else _viewModel.TogglePause();
    }
    private void OnExpandPlayer(object? s, TappedEventArgs e) { if (_viewModel.HasCurrentTrack) IsPlayerExpanded = true; }
    private void OnCollapsePlayer(object? s, EventArgs e) { _isSeeking = false; IsPlayerExpanded = false; }
    private void OnSeekStarted(object? s, EventArgs e) => _isSeeking = true;
    private void OnSeekValueChanged(object? s, ValueChangedEventArgs e) { if (_isSeeking) _viewModel.PreviewSeek(e.NewValue); }
    private void OnSeekCompleted(object? s, EventArgs e)
    {
        var target = PlaybackSlider.Value * _viewModel.DurationMs;
        _isSeeking = false;
        _viewModel.Seek(target);
    }
    private void ShowLibrary()
    {
        _viewModel.CancelSearch();
        TracksList.ItemsSource = _viewModel.Library; _isSearching = false;
        SectionTitle.Text = "Моя музыка";
        EmptyMusicLabel.Text = "Ваша музыка начинается здесь";
    }
    private Task StartSelectedTrackAsync(MusicTrackResponse track)
    {
        IsPlayerExpanded = true; _isSeeking = false;
        if (_viewModel.CurrentTrack?.Id == track.Id)
            return _viewModel.HasPlaybackError ? _viewModel.RetryAsync() : Task.CompletedTask;
        return _viewModel.PlayFromQueueAsync(track, _isSearching ? _viewModel.SearchResults : _viewModel.Library,
            _isSearching ? $"Поиск: {_viewModel.SearchText}" : "Моя музыка");
    }
    private async void OnPlayAllClicked(object? s, EventArgs e)
    {
        var tracks = _isSearching ? _viewModel.SearchResults : _viewModel.Library;
        if (tracks.FirstOrDefault() is { } track) await StartSelectedTrackAsync(track);
    }
    private async void OnNextClicked(object? s, EventArgs e) { _isSeeking = false; await _viewModel.NextAsync(); }
    private async void OnPreviousClicked(object? s, EventArgs e) { _isSeeking = false; await _viewModel.PreviousAsync(); }
    private void OnShuffleClicked(object? s, EventArgs e) => _viewModel.ToggleShuffle();
    private void OnRepeatClicked(object? s, EventArgs e) => _viewModel.CycleRepeat();
    private async void OnRetryClicked(object? s, EventArgs e) => await _viewModel.RetryAsync();
    private async void OnToggleSavedClicked(object? s, EventArgs e)
    {
        if (_isSaving) return;
        _isSaving = true;
        try { await SafeAsync(_viewModel.ToggleSavedAsync); }
        finally { _isSaving = false; }
    }
    private void OnPlayerSwipedDown(object? s, SwipedEventArgs e) { if (!_isSeeking) IsPlayerExpanded = false; }
    private void OnOpenQueue(object? s, TappedEventArgs e)
    {
        QueueSheet.HeightRequest = Math.Min(400, Height * 0.65);
        QueueOverlay.IsVisible = true;
        if (_viewModel.CurrentTrack is { } track) QueueList.ScrollTo(track, position: ScrollToPosition.Center, animate: false);
    }
    private void OnCloseQueue(object? s, TappedEventArgs e) => QueueOverlay.IsVisible = false;
    private void OnCloseQueueClicked(object? s, EventArgs e) => QueueOverlay.IsVisible = false;
    private async void OnQueueTrackSelected(object? s, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MusicTrackResponse track) return;
        QueueList.SelectedItem = null; QueueOverlay.IsVisible = false; _isSeeking = false;
        await _viewModel.PlayQueueTrackAsync(track);
    }
    private void OnArtworkAreaSizeChanged(object? s, EventArgs e)
    {
        if (ArtworkArea.Width <= 0) return;
        var size = Math.Clamp(Math.Min(ArtworkArea.Width, Height * 0.39), 160, 340);
        HeroArtwork.WidthRequest = HeroArtwork.HeightRequest = size;
    }
    private async void OnTrackMenuClicked(object? s, EventArgs e)
    {
        if ((s as ImageButton)?.CommandParameter is MusicTrackResponse track) await ShowTrackMenuAsync(track);
    }
    private async void OnCurrentTrackMenuClicked(object? s, EventArgs e)
    {
        if (_viewModel.CurrentTrack is { } track) await ShowTrackMenuAsync(track);
    }
    private async Task ShowTrackMenuAsync(MusicTrackResponse track)
    {
        var saved = _viewModel.Library.Any(x => x.Id == track.Id);
        var libraryAction = saved ? "Убрать из моей музыки" : "Добавить в мою музыку";
        var action = await DisplayActionSheetAsync(track.Title, "Отмена", null, libraryAction, "Скачать трек");
        if (action == libraryAction) await SafeAsync(ct => saved ? _viewModel.RemoveAsync(track, ct) : _viewModel.AddAsync(track, ct));
        if (action == "Скачать трек") await SafeAsync(ct => _viewModel.DownloadAsync(track, ct));
    }
    private async void OnUploadClicked(object? s, EventArgs e)
    {
        if (!UploadButton.IsEnabled) return;
        UploadButton.IsEnabled = false;
        try
        {
            await SafeAsync(async ct =>
            {
                var selection = await _musicFiles.PickAsync(ct);
                if (selection is null) return;
                var title = await DisplayPromptAsync("Трек", "Название", initialValue: Path.GetFileNameWithoutExtension(selection.FileName));
                if (string.IsNullOrWhiteSpace(title)) return;
                var artist = await DisplayPromptAsync("Трек", "Исполнитель");
                if (string.IsNullOrWhiteSpace(artist)) return;
                UploadButton.Text = "Загрузка…";
                var stored = await _uploader.UploadAsync(selection, null, ct);
                await _viewModel.UploadAsync(stored.Id, title.Trim(), artist.Trim(), selection.DurationMs!.Value, null, ct);
            });
        }
        finally
        {
            UploadButton.Text = "Загрузить";
            UploadButton.IsEnabled = true;
        }
    }
    private async Task SafeAsync(Func<CancellationToken, Task> action) { try { await action(CancellationToken.None); } catch (Exception ex) { await DisplayAlertAsync("Ошибка", ex.Message, "OK"); } }
}
