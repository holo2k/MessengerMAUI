using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Music;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Music;

public enum MusicRepeatMode { Off, All, One }

public sealed class MusicViewModel : ObservableObject
{
    private readonly IMusicApi api;
    private readonly IMusicDownloadService downloads;
    private readonly IAudioPlayerService player;
    private readonly IAsyncDelay delay;
    private CancellationTokenSource? _searchCancellation, _playCancellation;
    private string _searchText = string.Empty, _status = string.Empty, _playbackError = string.Empty;
    private double _downloadProgress, _positionMs, _durationMs;
    private MusicTrackResponse? _currentTrack;
    private List<MusicTrackResponse> _orderedQueue = [];
    private int _queueIndex = -1, _playVersion;
    private bool _isPreparing, _shuffle, _isSearchLoading;
    private MusicRepeatMode _repeat;
    private string _queueSource = "Моя музыка";
    private Guid _currentUserId;

    public event Action<MusicTrackResponse>? TrackUpdated;
    public void SetCurrentUser(Guid userId) => _currentUserId = userId;
    public bool CanEditCover(MusicTrackResponse track) =>
        _currentUserId != Guid.Empty && track.UploaderUserId == _currentUserId && track.Status == "available";

    public MusicViewModel(IMusicApi api, IMusicDownloadService downloads, IAudioPlayerService player, IAsyncDelay delay)
    {
        this.api = api; this.downloads = downloads; this.player = player; this.delay = delay;
        player.PlaybackEnded += OnPlaybackEnded;
        Library.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsCurrentTrackSaved));
    }

    public ObservableCollection<MusicTrackResponse> SearchResults { get; } = [];
    public ObservableCollection<MusicTrackResponse> Library { get; } = [];
    public ObservableCollection<MusicTrackResponse> Queue { get; } = [];
    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsSearchLoading { get => _isSearchLoading; private set => SetProperty(ref _isSearchLoading, value); }
    public double DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, value); }
    public bool IsPlaying => !_isPreparing && player.IsPlaying;
    public bool IsPreparing => _isPreparing;
    public bool IsBuffering => _isPreparing || player.IsBuffering;
    public bool IsTimingLoading => HasCurrentTrack && !HasPlaybackError && (_isPreparing || _durationMs <= 0);
    public bool CanSeek => HasCurrentTrack && !_isPreparing && !HasPlaybackError && _durationMs > 0;
    public bool CanTogglePlayback => HasCurrentTrack && !_isPreparing;
    public double BufferedProgress => _isPreparing ? 0 : Math.Clamp(player.BufferedProgress, PlaybackProgress, 1);
    public string PlaybackError => _playbackError;
    public bool HasPlaybackError => !string.IsNullOrEmpty(_playbackError);
    public string PlaybackStateText => HasPlaybackError ? "Не удалось воспроизвести" : IsBuffering ? "Буферизация…" : IsPlaying ? "Сейчас играет" : "На паузе";
    public MusicTrackResponse? CurrentTrack => _currentTrack;
    public bool HasCurrentTrack => _currentTrack is not null;
    public bool IsCurrentTrackSaved => _currentTrack is not null && Library.Any(x => x.Id == _currentTrack.Id);
    public double PositionMs => _positionMs;
    public double DurationMs => _durationMs;
    public double PlaybackProgress => _durationMs > 0 ? _positionMs / _durationMs : 0;
    public string PositionText => PlaybackTime.Format(_positionMs);
    public string DurationText => PlaybackTime.Format(_durationMs);
    public string QueueSource => _queueSource;
    public string QueuePositionText => Queue.Count == 0 ? string.Empty : $"{_queueIndex + 1} из {Queue.Count}";
    public bool CanGoNext => Queue.Count > 1 && (_queueIndex < Queue.Count - 1 || _repeat == MusicRepeatMode.All);
    public bool CanGoPrevious => HasCurrentTrack;
    public bool IsShuffleEnabled => _shuffle;
    public bool IsRepeatEnabled => _repeat != MusicRepeatMode.Off;
    public string RepeatLabel => _repeat switch { MusicRepeatMode.All => "Повтор очереди", MusicRepeatMode.One => "Повтор трека", _ => "Повтор выключен" };
    public bool IsRepeatOne => _repeat == MusicRepeatMode.One;

    public async Task SearchAsync(CancellationToken ct = default)
    {
        _searchCancellation?.Cancel(); _searchCancellation?.Dispose();
        _searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var cancellation = _searchCancellation;
        var token = cancellation.Token;
        var query = SearchText.Trim();
        IsSearchLoading = true;
        try
        {
            await Task.Yield(); await delay.WaitAsync(TimeSpan.FromMilliseconds(300), token);
            var rows = await api.SearchAsync(query, token);
            token.ThrowIfCancellationRequested();
            SearchResults.Clear(); foreach (var row in rows) SearchResults.Add(row);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { if (ReferenceEquals(cancellation, _searchCancellation)) IsSearchLoading = false; }
    }
    public void CancelSearch() { _searchCancellation?.Cancel(); IsSearchLoading = false; }
    public async Task LoadLibraryAsync(CancellationToken ct = default)
    {
        var rows = await api.LibraryAsync(ct);
        Library.Clear(); foreach (var row in rows) Library.Add(row);
        foreach (var row in rows) ApplyTrackUpdate(row);
    }
    public async Task AddAsync(MusicTrackResponse track, CancellationToken ct = default) { await api.AddAsync(track.Id, ct); if (Library.All(x => x.Id != track.Id)) Library.Add(track); }
    public async Task RemoveAsync(MusicTrackResponse track, CancellationToken ct = default) { await api.RemoveAsync(track.Id, ct); var item = Library.FirstOrDefault(x => x.Id == track.Id); if (item is not null) Library.Remove(item); }
    public async Task ToggleSavedAsync(CancellationToken ct = default)
    {
        if (_currentTrack is not { } track) return;
        if (IsCurrentTrackSaved) await RemoveAsync(track, ct); else await AddAsync(track, ct);
    }
    public async Task UploadAsync(Guid audioId, string title, string artist, long duration, Guid? cover, CancellationToken ct = default)
    {
        var track = await api.CreateAsync(audioId, title, artist, duration, cover, ct);
        if (Library.All(x => x.Id != track.Id)) Library.Insert(0, track);
        Status = "Трек загружен";
    }
    public async Task UpdateCoverAsync(MusicTrackResponse track, Guid coverObjectId, CancellationToken ct = default)
    {
        var updated = await api.UpdateCoverAsync(track.Id, coverObjectId, ct);
        ApplyTrackUpdate(updated);
        Status = "Обложка обновлена";
    }
    public void ApplyTrackUpdate(MusicTrackResponse track)
    {
        foreach (var rows in new[] { Library, SearchResults, Queue })
            for (var index = 0; index < rows.Count; index++)
                if (rows[index].Id == track.Id && rows[index] != track) rows[index] = track;
        for (var index = 0; index < _orderedQueue.Count; index++)
            if (_orderedQueue[index].Id == track.Id) _orderedQueue[index] = track;
        if (_currentTrack?.Id == track.Id && _currentTrack != track)
        {
            _currentTrack = track;
            NotifyCurrentTrack();
        }
        TrackUpdated?.Invoke(track);
    }
    public async Task DownloadAsync(MusicTrackResponse track, CancellationToken ct = default) { DownloadProgress = 0; await downloads.DownloadAsync(track, new InlineProgress(x => DownloadProgress = x), ct); Status = "Трек скачан"; }
    public Task RemoveDownloadAsync(MusicTrackResponse track) => downloads.RemoveAsync(track.Id);

    public Task PlayAsync(MusicTrackResponse track, CancellationToken ct = default) =>
        PlayFromQueueAsync(track, Library.Any(x => x.Id == track.Id) ? Library : new[] { track }, "Моя музыка", ct);

    public Task PlayFromQueueAsync(MusicTrackResponse track, IEnumerable<MusicTrackResponse> tracks, string source, CancellationToken ct = default)
    {
        _orderedQueue = tracks.Where(x => x.Status == "available").DistinctBy(x => x.Id).ToList();
        if (_orderedQueue.All(x => x.Id != track.Id)) _orderedQueue.Insert(0, track);
        _queueSource = source;
        RebuildQueue(track.Id);
        return StartTrackAsync(track, ct);
    }

    private async Task StartTrackAsync(MusicTrackResponse track, CancellationToken ct = default)
    {
        var version = ++_playVersion;
        _playCancellation?.Cancel(); _playCancellation?.Dispose();
        _playCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _playCancellation.Token;
        player.Stop();
        _currentTrack = track;
        _queueIndex = Queue.ToList().FindIndex(x => x.Id == track.Id);
        _positionMs = _durationMs = 0;
        _playbackError = string.Empty;
        _isPreparing = true;
        NotifyCurrentTrack();
        NotifyPlayback();
        try
        {
            var auth = await api.AuthorizeAsync(track.Id, false, token);
            token.ThrowIfCancellationRequested();
            if (version != _playVersion) return;
            ChatNotePlayback.Activate(this);
            await player.PlayAsync(auth.Url, token);
            if (version != _playVersion) return;
            _isPreparing = false;
            RefreshPlayback();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (version != _playVersion) return;
            _isPreparing = false;
            _playbackError = "Проверьте соединение и попробуйте снова.";
            NotifyPlayback();
        }
        finally
        {
            if (version == _playVersion && token.IsCancellationRequested) { _isPreparing = false; NotifyPlayback(); }
        }
    }
    public Task RetryAsync(CancellationToken ct = default) => _currentTrack is { } track ? StartTrackAsync(track, ct) : Task.CompletedTask;
    public Task NextAsync(CancellationToken ct = default)
    {
        if (!CanGoNext) return Task.CompletedTask;
        return StartTrackAsync(Queue[(_queueIndex + 1) % Queue.Count], ct);
    }
    public Task PreviousAsync(CancellationToken ct = default)
    {
        if (!HasCurrentTrack) return Task.CompletedTask;
        if (!_isPreparing && _positionMs > 3000) { Seek(0); return Task.CompletedTask; }
        if (_queueIndex > 0) return StartTrackAsync(Queue[_queueIndex - 1], ct);
        return RetryAsync(ct);
    }
    public Task PlayQueueTrackAsync(MusicTrackResponse track, CancellationToken ct = default) =>
        Queue.Any(x => x.Id == track.Id) ? StartTrackAsync(track, ct) : Task.CompletedTask;
    public async Task HandlePlaybackEndedAsync()
    {
        if (_isPreparing || HasPlaybackError || !HasCurrentTrack) return;
        if (_repeat == MusicRepeatMode.One) await RetryAsync();
        else if (CanGoNext) await NextAsync();
        else { UpdatePosition(_durationMs); NotifyPlayback(); }
    }
    private async void OnPlaybackEnded(object? sender, EventArgs e)
    {
        try { await HandlePlaybackEndedAsync(); }
        catch (Exception) { _playbackError = "Не удалось перейти к следующему треку."; NotifyPlayback(); }
    }
    public void ToggleShuffle()
    {
        _shuffle = !_shuffle;
        RebuildQueue(_currentTrack?.Id);
        OnPropertyChanged(nameof(IsShuffleEnabled));
    }
    private void RebuildQueue(Guid? currentId)
    {
        var rows = _orderedQueue.ToList();
        if (_shuffle && currentId is { } id)
        {
            var current = rows.FirstOrDefault(x => x.Id == id);
            rows.RemoveAll(x => x.Id == id);
            Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows));
            if (current is not null) rows.Insert(0, current);
        }
        Queue.Clear(); foreach (var row in rows) Queue.Add(row);
        _queueIndex = rows.FindIndex(x => x.Id == currentId);
        NotifyQueue();
    }
    public void CycleRepeat()
    {
        _repeat = (MusicRepeatMode)(((int)_repeat + 1) % 3);
        OnPropertyChanged(nameof(IsRepeatEnabled)); OnPropertyChanged(nameof(IsRepeatOne)); OnPropertyChanged(nameof(RepeatLabel)); NotifyQueue();
    }
    public void TogglePause()
    {
        if (!CanTogglePlayback || HasPlaybackError) return;
        if (player.IsPlaying) player.Pause();
        else { ChatNotePlayback.Activate(this); if (_durationMs > 0 && _positionMs >= _durationMs) player.Seek(0); player.Resume(); }
        RefreshPlayback();
    }
    public void RefreshPlayback()
    {
        if (!_isPreparing)
        {
            var duration = player.DurationMs;
            _durationMs = HasCurrentTrack ? (double.IsFinite(duration) && duration > 0 ? duration : Math.Max(0, _currentTrack!.DurationMs)) : 0;
            UpdatePosition(HasCurrentTrack && double.IsFinite(player.PositionMs) ? player.PositionMs : 0);
            if (player.PlaybackError is { Length: > 0 } error) _playbackError = error;
        }
        NotifyPlayback();
    }
    public void Seek(double milliseconds)
    {
        if (!CanSeek || !double.IsFinite(milliseconds)) return;
        var target = Math.Clamp(milliseconds, 0, _durationMs);
        player.Seek(target); UpdatePosition(target);
    }
    public void PreviewSeek(double fraction)
    {
        if (CanSeek && double.IsFinite(fraction)) UpdatePosition(Math.Clamp(fraction, 0, 1) * _durationMs);
    }
    private void UpdatePosition(double milliseconds)
    {
        _positionMs = Math.Clamp(milliseconds, 0, _durationMs);
        OnPropertyChanged(nameof(PositionMs)); OnPropertyChanged(nameof(PositionText)); OnPropertyChanged(nameof(PlaybackProgress));
    }
    private void NotifyPlayback()
    {
        foreach (var name in new[] { nameof(PositionText), nameof(DurationMs), nameof(DurationText), nameof(PlaybackProgress), nameof(BufferedProgress), nameof(IsPlaying), nameof(IsPreparing), nameof(IsBuffering), nameof(IsTimingLoading), nameof(CanSeek), nameof(CanTogglePlayback), nameof(PlaybackError), nameof(HasPlaybackError), nameof(PlaybackStateText) }) OnPropertyChanged(name);
    }
    private void NotifyQueue()
    {
        OnPropertyChanged(nameof(QueueSource)); OnPropertyChanged(nameof(QueuePositionText)); OnPropertyChanged(nameof(CanGoNext)); OnPropertyChanged(nameof(CanGoPrevious));
    }
    private void NotifyCurrentTrack()
    {
        OnPropertyChanged(nameof(CurrentTrack)); OnPropertyChanged(nameof(HasCurrentTrack)); OnPropertyChanged(nameof(IsCurrentTrackSaved)); NotifyQueue();
    }
    public async Task OnTrackBlockedAsync(Guid trackId, string reason)
    {
        var libraryItem = Library.FirstOrDefault(x => x.Id == trackId);
        if (libraryItem is not null) Library.Remove(libraryItem);
        var searchItem = SearchResults.FirstOrDefault(x => x.Id == trackId);
        if (searchItem is not null) SearchResults.Remove(searchItem);
        _orderedQueue.RemoveAll(x => x.Id == trackId);
        var queued = Queue.FirstOrDefault(x => x.Id == trackId);
        if (queued is not null) Queue.Remove(queued);
        if (_currentTrack?.Id == trackId)
        {
            ++_playVersion; _playCancellation?.Cancel(); player.Stop(); _currentTrack = null; _isPreparing = false;
            _positionMs = _durationMs = 0; NotifyCurrentTrack(); NotifyPlayback();
        }
        else { _queueIndex = Queue.ToList().FindIndex(x => x.Id == _currentTrack?.Id); NotifyQueue(); }
        await downloads.InvalidateAsync(trackId); Status = reason;
    }
    private sealed class InlineProgress(Action<double> action) : IProgress<double> { public void Report(double value) => action(value); }
}
