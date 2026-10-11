using Messenger.Contracts.Media;
using Messenger.Contracts.Music;
using Messenger.Maui.Features.Music;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Music;

public sealed class MusicViewModelTests
{
    [Fact]
    public async Task Search_is_debounced_and_upload_does_not_declare_rights()
    {
        var api = new FakeMusicApi(); var delay = new ControlledDebounceDelay(); var vm = new MusicViewModel(api, new FakeDownloads(), new FakePlayer(), delay);
        vm.SearchText = "пе"; var first = vm.SearchAsync(); await delay.FirstCallStarted; vm.SearchText = "песня"; await vm.SearchAsync(); await first;
        var track = vm.SearchResults.Single();
        await vm.AddAsync(track); await vm.RemoveAsync(track);
        await vm.UploadAsync(Guid.NewGuid(), "Новая", "Автор", 1000, null);
        Assert.Equal(1, api.SearchCalls); Assert.Equal(1, api.AddCalls); Assert.Equal(1, api.RemoveCalls); Assert.Equal(0, api.Declarations);
        Assert.Equal(1, api.CreateCalls);
        Assert.Equal("Трек загружен", vm.Status);
        Assert.Single(vm.Library);
        Assert.Equal("available", vm.Library[0].Status);
    }

    [Fact]
    public async Task Download_progress_play_pause_seek_and_time_format_work()
    {
        var downloads = new FakeDownloads(); var player = new FakePlayer(); var vm = new MusicViewModel(new FakeMusicApi(), downloads, player, new ImmediateDelay());
        var track = Track();
        await vm.DownloadAsync(track); await vm.PlayAsync(track); vm.TogglePause(); vm.Seek(61_000);
        Assert.Equal(1, vm.DownloadProgress); Assert.False(vm.IsPlaying); Assert.Equal("01:01", vm.PositionText);
        await vm.RemoveDownloadAsync(track); Assert.Equal(1, downloads.RemoveCalls);
    }

    [Fact]
    public async Task Playback_time_and_progress_follow_native_player_and_pause_state()
    {
        var player = new FakePlayer { DurationMs = 185_000 };
        var vm = new MusicViewModel(new FakeMusicApi(), new FakeDownloads(), player, new ImmediateDelay());
        await vm.PlayAsync(Track());
        player.PositionMs = 61_000;
        vm.RefreshPlayback();
        Assert.Equal("01:01", vm.PositionText);
        Assert.Equal("03:05", vm.DurationText);
        Assert.Equal(61_000d / 185_000, vm.PlaybackProgress, 6);
        vm.TogglePause();
        Assert.False(vm.IsPlaying);
        vm.RefreshPlayback();
        Assert.Equal("01:01", vm.PositionText);
        player.PositionMs = 185_000;
        player.Pause();
        vm.RefreshPlayback();
        vm.TogglePause();
        Assert.True(vm.IsPlaying);
        Assert.Equal("00:00", vm.PositionText);
    }

    [Fact]
    public async Task Seek_is_clamped_and_new_track_resets_position()
    {
        var player = new FakePlayer();
        var vm = new MusicViewModel(new FakeMusicApi(), new FakeDownloads(), player, new ImmediateDelay());
        await vm.PlayAsync(Track());
        vm.Seek(999_999);
        Assert.Equal(120_000, player.PositionMs);
        Assert.Equal(1, vm.PlaybackProgress);
        vm.Seek(-100);
        Assert.Equal(0, player.PositionMs);
        vm.Seek(double.NaN);
        Assert.Equal(0, player.PositionMs);
        vm.Seek(60_000);
        await vm.PlayAsync(Track());
        Assert.Equal("00:00", vm.PositionText);
        Assert.Equal(0, vm.PlaybackProgress);
    }

    [Fact]
    public async Task Long_tracks_display_hours_and_blocking_resets_progress()
    {
        var player = new FakePlayer { DurationMs = 3_661_000 };
        var vm = new MusicViewModel(new FakeMusicApi(), new FakeDownloads(), player, new ImmediateDelay());
        var track = Track();
        await vm.PlayAsync(track);
        player.PositionMs = 3_600_000;
        vm.RefreshPlayback();
        Assert.Equal("1:01:01", vm.DurationText);
        Assert.Equal("1:00:00", vm.PositionText);
        await vm.OnTrackBlockedAsync(track.Id, "Удалено");
        Assert.False(vm.HasCurrentTrack);
        Assert.Equal(0, vm.PlaybackProgress);
        Assert.Equal("00:00", vm.DurationText);
    }

    [Fact]
    public async Task Blocked_playing_track_stops_and_discards_authorization_with_reason()
    {
        var downloads = new FakeDownloads(); var player = new FakePlayer(); var vm = new MusicViewModel(new FakeMusicApi(), downloads, player, new ImmediateDelay());
        var track = Track(); await vm.PlayAsync(track);
        await vm.OnTrackBlockedAsync(track.Id, "Удалено по требованию правообладателя");
        Assert.Equal(2, player.StopCalls); Assert.Equal(1, downloads.InvalidateCalls); Assert.Contains("правообладателя", vm.Status);
    }

    private static MusicTrackResponse Track() => new(Guid.NewGuid(), "Песня", "Автор", 120000, null, "available");

    [Fact]
    public async Task Track_and_skeleton_are_visible_before_authorization_and_preparation_complete()
    {
        var authorization = new TaskCompletionSource<DownloadAuthorizationResponse>();
        var preparation = new TaskCompletionSource();
        var api = new FakeMusicApi { AuthorizeOperation = (_, ct) => authorization.Task.WaitAsync(ct) };
        var player = new FakePlayer { PlayOperation = (_, ct) => preparation.Task.WaitAsync(ct), DurationMs = 185_000 };
        var vm = new MusicViewModel(api, new FakeDownloads(), player, new ImmediateDelay());
        var track = Track();
        var loading = vm.PlayFromQueueAsync(track, new[] { track }, "Поиск");
        Assert.Equal(track.Id, vm.CurrentTrack!.Id);
        Assert.True(vm.IsPreparing);
        Assert.True(vm.IsTimingLoading);
        Assert.False(vm.CanSeek);
        authorization.SetResult(new DownloadAuthorizationResponse("https://audio/one", DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.True(vm.IsTimingLoading);
        preparation.SetResult();
        await loading;
        Assert.False(vm.IsTimingLoading);
        Assert.Equal("03:05", vm.DurationText);
        Assert.True(vm.CanSeek);
    }

    [Fact]
    public async Task Rapid_selection_cancels_old_request_and_keeps_latest_metadata()
    {
        var firstAuthorization = new TaskCompletionSource<DownloadAuthorizationResponse>();
        var first = Track(); var second = Track();
        var api = new FakeMusicApi { AuthorizeOperation = (id, ct) => id == first.Id
            ? firstAuthorization.Task.WaitAsync(ct)
            : Task.FromResult(new DownloadAuthorizationResponse("https://audio/second", DateTimeOffset.UtcNow.AddMinutes(5))) };
        var player = new FakePlayer();
        var vm = new MusicViewModel(api, new FakeDownloads(), player, new ImmediateDelay());
        var old = vm.PlayFromQueueAsync(first, new[] { first, second }, "Моя музыка");
        await vm.PlayFromQueueAsync(second, new[] { first, second }, "Моя музыка");
        await old;
        Assert.Equal(second.Id, vm.CurrentTrack!.Id);
        Assert.False(vm.IsPreparing);
        Assert.False(vm.HasPlaybackError);
        Assert.Equal(new[] { "https://audio/second" }, player.PlayedSources);
    }

    [Fact]
    public async Task Selecting_next_during_native_preparation_cancels_old_player_load()
    {
        var waiting = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var calls = 0;
        var player = new FakePlayer { PlayOperation = (_, ct) =>
        {
            if (++calls != 1) return Task.CompletedTask;
            started.SetResult();
            return waiting.Task.WaitAsync(ct);
        } };
        var vm = new MusicViewModel(new FakeMusicApi(), new FakeDownloads(), player, new ImmediateDelay());
        var first = Track(); var second = Track();
        var initial = vm.PlayFromQueueAsync(first, new[] { first, second }, "Моя музыка");
        await started.Task;
        Assert.True(vm.IsTimingLoading);
        await vm.NextAsync();
        await initial;
        Assert.Equal(second.Id, vm.CurrentTrack!.Id);
        Assert.False(vm.IsTimingLoading);
        Assert.True(vm.IsPlaying);
        Assert.False(vm.HasPlaybackError);
    }

    [Fact]
    public async Task Previous_restarts_after_three_seconds_and_otherwise_moves_to_previous_track()
    {
        var first = Track(); var second = Track(); var third = Track();
        var player = new FakePlayer();
        var vm = new MusicViewModel(new FakeMusicApi(), new FakeDownloads(), player, new ImmediateDelay());
        await vm.PlayFromQueueAsync(second, new[] { first, second, third }, "Моя музыка");
        player.PositionMs = 10_000; vm.RefreshPlayback();
        await vm.PreviousAsync();
        Assert.Equal(second.Id, vm.CurrentTrack!.Id);
        Assert.Equal(0, vm.PositionMs);
        await vm.PreviousAsync();
        Assert.Equal(first.Id, vm.CurrentTrack!.Id);
        await vm.NextAsync();
        Assert.Equal(second.Id, vm.CurrentTrack!.Id);
        Assert.Equal("2 из 3", vm.QueuePositionText);
    }

    [Fact]
    public async Task Completion_advances_queue_and_repeat_modes_handle_boundaries()
    {
        var first = Track(); var second = Track();
        var player = new FakePlayer();
        var vm = new MusicViewModel(new FakeMusicApi(), new FakeDownloads(), player, new ImmediateDelay());
        await vm.PlayFromQueueAsync(first, new[] { first, second }, "Моя музыка");
        player.Finish();
        Assert.Equal(second.Id, vm.CurrentTrack!.Id);
        Assert.False(vm.CanGoNext);
        player.Finish();
        Assert.Equal(second.Id, vm.CurrentTrack!.Id);
        Assert.False(vm.IsPlaying);
        vm.CycleRepeat(); // Repeat all wraps to first.
        await vm.NextAsync();
        Assert.Equal(first.Id, vm.CurrentTrack!.Id);
        vm.CycleRepeat(); // Repeat one restarts the same track.
        player.Finish();
        Assert.Equal(first.Id, vm.CurrentTrack!.Id);
        Assert.True(vm.IsPlaying);
        Assert.Equal(0, vm.PositionMs);
        await vm.NextAsync(); // Manual next still skips when repeating one.
        Assert.Equal(second.Id, vm.CurrentTrack!.Id);
    }

    [Fact]
    public async Task Shuffle_preserves_queue_members_and_disabling_it_restores_original_order()
    {
        var tracks = Enumerable.Range(0, 5).Select(_ => Track()).ToArray();
        var vm = new MusicViewModel(new FakeMusicApi(), new FakeDownloads(), new FakePlayer(), new ImmediateDelay());
        await vm.PlayFromQueueAsync(tracks[2], tracks, "Результаты поиска");
        vm.ToggleShuffle();
        Assert.Equal(tracks[2].Id, vm.Queue[0].Id);
        Assert.Equal(tracks.Select(x => x.Id).Order(), vm.Queue.Select(x => x.Id).Order());
        vm.ToggleShuffle();
        Assert.Equal(tracks.Select(x => x.Id), vm.Queue.Select(x => x.Id));
        Assert.Equal("3 из 5", vm.QueuePositionText);
    }

    [Fact]
    public async Task Network_failure_keeps_selected_track_and_retry_clears_error()
    {
        var api = new FakeMusicApi { AuthorizeOperation = (_, _) => throw new HttpRequestException("offline") };
        var vm = new MusicViewModel(api, new FakeDownloads(), new FakePlayer(), new ImmediateDelay());
        var track = Track();
        await vm.PlayAsync(track);
        Assert.Equal(track.Id, vm.CurrentTrack!.Id);
        Assert.True(vm.HasPlaybackError);
        Assert.False(vm.IsPreparing);
        api.AuthorizeOperation = null;
        await vm.RetryAsync();
        Assert.False(vm.HasPlaybackError);
        Assert.True(vm.IsPlaying);
    }

    [Fact]
    public async Task Mid_track_buffering_keeps_time_and_controls_available()
    {
        var player = new FakePlayer { DurationMs = 100_000, BufferedProgress = 0.6 };
        var vm = new MusicViewModel(new FakeMusicApi(), new FakeDownloads(), player, new ImmediateDelay());
        await vm.PlayAsync(Track());
        player.PositionMs = 25_000; player.IsBuffering = true; vm.RefreshPlayback();
        Assert.True(vm.IsBuffering);
        Assert.False(vm.IsTimingLoading);
        Assert.True(vm.CanTogglePlayback);
        Assert.Equal(0.25, vm.PlaybackProgress);
        Assert.Equal(0.6, vm.BufferedProgress);
        vm.TogglePause();
        Assert.False(vm.IsPlaying);
    }
    private sealed class ImmediateDelay : IAsyncDelay { public Task WaitAsync(TimeSpan delay, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; } }
    private sealed class ControlledDebounceDelay : IAsyncDelay
    {
        private readonly TaskCompletionSource _firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public Task FirstCallStarted => _firstCallStarted.Task;
        public async Task WaitAsync(TimeSpan delay, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _firstCallStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
        }
    }
    private sealed class FakeMusicApi : IMusicApi
    {
        public int SearchCalls, AddCalls, RemoveCalls, Declarations, CreateCalls;
        public Func<Guid, CancellationToken, Task<DownloadAuthorizationResponse>>? AuthorizeOperation { get; set; }
        public Task<IReadOnlyList<MusicTrackResponse>> SearchAsync(string q, CancellationToken ct) { SearchCalls++; return Task.FromResult<IReadOnlyList<MusicTrackResponse>>([Track()]); }
        public Task<IReadOnlyList<MusicTrackResponse>> LibraryAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MusicTrackResponse>>([]);
        public Task AddAsync(Guid id, CancellationToken ct) { AddCalls++; return Task.CompletedTask; }
        public Task RemoveAsync(Guid id, CancellationToken ct) { RemoveCalls++; return Task.CompletedTask; }
        public Task<MusicTrackResponse> CreateAsync(Guid audio, string title, string artist, long duration, Guid? cover, CancellationToken ct) { CreateCalls++; return Task.FromResult(Track()); }
        public Task DeclareAsync(Guid id, string statement, CancellationToken ct) { Declarations++; return Task.CompletedTask; }
        public Task<DownloadAuthorizationResponse> AuthorizeAsync(Guid id, bool download, CancellationToken ct) => AuthorizeOperation?.Invoke(id, ct) ?? Task.FromResult(new DownloadAuthorizationResponse("https://audio", DateTimeOffset.UtcNow.AddMinutes(5)));
    }
    private sealed class FakeDownloads : IMusicDownloadService
    {
        public int RemoveCalls, InvalidateCalls;
        public Task<string> DownloadAsync(MusicTrackResponse track, IProgress<double> progress, CancellationToken ct) { progress.Report(1); return Task.FromResult("track.mp3"); }
        public Task RemoveAsync(Guid id) { RemoveCalls++; return Task.CompletedTask; }
        public Task InvalidateAsync(Guid id) { InvalidateCalls++; return Task.CompletedTask; }
    }
    private sealed class FakePlayer : IAudioPlayerService
    {
        public bool IsPlaying { get; private set; } public int StopCalls;
        public double PositionMs { get; set; }
        public double DurationMs { get; set; }
        public bool IsBuffering { get; set; }
        public double BufferedProgress { get; set; } = 1;
        public string? PlaybackError { get; set; }
        public event EventHandler? PlaybackEnded;
        public Func<string, CancellationToken, Task>? PlayOperation { get; set; }
        public List<string> PlayedSources { get; } = [];
        public async Task PlayAsync(string source, CancellationToken ct)
        {
            PlayedSources.Add(source);
            if (PlayOperation is not null) await PlayOperation(source, ct);
            PositionMs = 0; IsPlaying = true;
        }
        public void Finish() { PositionMs = DurationMs; IsPlaying = false; PlaybackEnded?.Invoke(this, EventArgs.Empty); }
        public void Pause() => IsPlaying = false; public void Resume() => IsPlaying = true;
        public void Seek(double milliseconds) { PositionMs = milliseconds; }
        public void Stop() { StopCalls++; PositionMs = 0; IsPlaying = false; }
    }
}
