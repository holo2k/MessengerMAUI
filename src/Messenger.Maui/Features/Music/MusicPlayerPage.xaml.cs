using System.ComponentModel;
using Messenger.Contracts.Music;

namespace Messenger.Maui.Features.Music;

public partial class MusicPlayerPage : ContentPage
{
    private readonly MusicViewModel _viewModel;
    private readonly Microsoft.Maui.Dispatching.IDispatcherTimer _playbackTimer;
    private bool _isSeeking, _isSaving, _isClosing;

    public Guid? CurrentTrackId => _viewModel.CurrentTrack?.Id;

    public MusicPlayerPage(MusicViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
        _playbackTimer = Dispatcher.CreateTimer();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(250);
        _playbackTimer.Tick += (_, _) =>
        {
            if (!_isSeeking) _viewModel.RefreshPlayback();
            if (!_viewModel.HasCurrentTrack) _ = ClosePlayerAsync();
        };
    }

    public static Task OpenAsync(INavigation navigation, MusicViewModel viewModel)
    {
        if (!viewModel.HasCurrentTrack || navigation.ModalStack.Any(page => page is MusicPlayerPage))
            return Task.CompletedTask;

        return navigation.PushModalAsync(new MusicPlayerPage(viewModel), animated: false);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.PropertyChanged += OnPlaybackPropertyChanged;
        _viewModel.RefreshPlayback();
        _playbackTimer.Start();
    }

    protected override void OnDisappearing()
    {
        _playbackTimer.Stop();
        _viewModel.PropertyChanged -= OnPlaybackPropertyChanged;
        _isSeeking = false;
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (QueueOverlay.IsVisible) QueueOverlay.IsVisible = false;
        else _ = ClosePlayerAsync();
        return true;
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MusicViewModel.CurrentTrack)) OnPropertyChanged(nameof(CurrentTrackId));
    }

    private async Task ClosePlayerAsync()
    {
        if (_isClosing) return;
        _isClosing = true;
        await Navigation.PopModalAsync(animated: false);
    }

    private async void OnCollapsePlayer(object? sender, EventArgs e) => await ClosePlayerAsync();
    private async void OnPlayerSwipedDown(object? sender, SwipedEventArgs e)
    {
        if (!_isSeeking) await ClosePlayerAsync();
    }

    private async void OnPauseClicked(object? sender, EventArgs e)
    {
        if (_viewModel.HasPlaybackError) await _viewModel.RetryAsync();
        else _viewModel.TogglePause();
    }

    private async void OnNextClicked(object? sender, EventArgs e) { _isSeeking = false; await _viewModel.NextAsync(); }
    private async void OnPreviousClicked(object? sender, EventArgs e) { _isSeeking = false; await _viewModel.PreviousAsync(); }
    private void OnShuffleClicked(object? sender, EventArgs e) => _viewModel.ToggleShuffle();
    private void OnRepeatClicked(object? sender, EventArgs e) => _viewModel.CycleRepeat();
    private async void OnRetryClicked(object? sender, EventArgs e) => await _viewModel.RetryAsync();

    private void OnSeekStarted(object? sender, EventArgs e) => _isSeeking = true;
    private void OnSeekValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_isSeeking) _viewModel.PreviewSeek(e.NewValue);
    }

    private void OnSeekCompleted(object? sender, EventArgs e)
    {
        var target = PlaybackSlider.Value * _viewModel.DurationMs;
        _isSeeking = false;
        _viewModel.Seek(target);
    }

    private async void OnToggleSavedClicked(object? sender, EventArgs e)
    {
        if (_isSaving) return;
        _isSaving = true;
        try { await SafeAsync(_viewModel.ToggleSavedAsync); }
        finally { _isSaving = false; }
    }

    private void OnOpenQueue(object? sender, TappedEventArgs e)
    {
        QueueSheet.HeightRequest = Math.Min(400, Height * 0.65);
        QueueOverlay.IsVisible = true;
        if (_viewModel.CurrentTrack is { } track) QueueList.ScrollTo(track, position: ScrollToPosition.Center, animate: false);
    }

    private void OnCloseQueue(object? sender, TappedEventArgs e) => QueueOverlay.IsVisible = false;
    private void OnCloseQueueClicked(object? sender, EventArgs e) => QueueOverlay.IsVisible = false;
    private async void OnQueueTrackSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MusicTrackResponse track) return;
        QueueList.SelectedItem = null;
        QueueOverlay.IsVisible = false;
        _isSeeking = false;
        await _viewModel.PlayQueueTrackAsync(track);
    }

    private void OnArtworkAreaSizeChanged(object? sender, EventArgs e)
    {
        if (ArtworkArea.Width <= 0) return;
        var size = Math.Clamp(Math.Min(ArtworkArea.Width, Height * 0.3), 140, 240);
        HeroArtwork.WidthRequest = HeroArtwork.HeightRequest = size;
    }

    private async void OnCurrentTrackMenuClicked(object? sender, EventArgs e)
    {
        if (_viewModel.CurrentTrack is not { } track) return;
        var saved = _viewModel.IsCurrentTrackSaved;
        var libraryAction = saved ? "Убрать из моей музыки" : "Добавить в мою музыку";
        var action = await DisplayActionSheetAsync(track.Title, "Отмена", null, libraryAction, "Скачать трек");
        if (action == libraryAction) await SafeAsync(ct => saved ? _viewModel.RemoveAsync(track, ct) : _viewModel.AddAsync(track, ct));
        if (action == "Скачать трек") await SafeAsync(ct => _viewModel.DownloadAsync(track, ct));
    }

    private async Task SafeAsync(Func<CancellationToken, Task> action)
    {
        try { await action(CancellationToken.None); }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }
}
