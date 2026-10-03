using Messenger.Contracts.Media;
using Messenger.Contracts.Music;
using Messenger.Maui.Features.Music;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Music;

public sealed class MusicViewModelTests
{
    [Fact]
    public async Task Search_is_debounced_and_library_add_remove_and_declaration_are_forwarded()
    {
        var api = new FakeMusicApi(); var vm = new MusicViewModel(api, new FakeDownloads(), new FakePlayer(), new ImmediateDelay());
        vm.SearchText = "пе"; var first = vm.SearchAsync(); vm.SearchText = "песня"; await vm.SearchAsync(); await first;
        var track = vm.SearchResults.Single();
        await vm.AddAsync(track); await vm.RemoveAsync(track);
        await vm.UploadAsync(Guid.NewGuid(), "Новая", "Автор", 1000, null, true);
        Assert.Equal(1, api.SearchCalls); Assert.Equal(1, api.AddCalls); Assert.Equal(1, api.RemoveCalls); Assert.Equal(1, api.Declarations);
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
    public async Task Blocked_playing_track_stops_and_discards_authorization_with_reason()
    {
        var downloads = new FakeDownloads(); var player = new FakePlayer(); var vm = new MusicViewModel(new FakeMusicApi(), downloads, player, new ImmediateDelay());
        var track = Track(); await vm.PlayAsync(track);
        await vm.OnTrackBlockedAsync(track.Id, "Удалено по требованию правообладателя");
        Assert.Equal(1, player.StopCalls); Assert.Equal(1, downloads.InvalidateCalls); Assert.Contains("правообладателя", vm.Status);
    }

    private static MusicTrackResponse Track() => new(Guid.NewGuid(), "Песня", "Автор", 120000, null, "available");
    private sealed class ImmediateDelay : IAsyncDelay { public Task WaitAsync(TimeSpan delay, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; } }
    private sealed class FakeMusicApi : IMusicApi
    {
        public int SearchCalls, AddCalls, RemoveCalls, Declarations;
        public Task<IReadOnlyList<MusicTrackResponse>> SearchAsync(string q, CancellationToken ct) { SearchCalls++; return Task.FromResult<IReadOnlyList<MusicTrackResponse>>([Track()]); }
        public Task<IReadOnlyList<MusicTrackResponse>> LibraryAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MusicTrackResponse>>([]);
        public Task AddAsync(Guid id, CancellationToken ct) { AddCalls++; return Task.CompletedTask; }
        public Task RemoveAsync(Guid id, CancellationToken ct) { RemoveCalls++; return Task.CompletedTask; }
        public Task<MusicTrackResponse> CreateAsync(Guid audio, string title, string artist, long duration, Guid? cover, CancellationToken ct) => Task.FromResult(Track());
        public Task DeclareAsync(Guid id, string statement, CancellationToken ct) { Declarations++; return Task.CompletedTask; }
        public Task<DownloadAuthorizationResponse> AuthorizeAsync(Guid id, bool download, CancellationToken ct) => Task.FromResult(new DownloadAuthorizationResponse("https://audio", DateTimeOffset.UtcNow.AddMinutes(5)));
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
        public Task PlayAsync(string source, CancellationToken ct) { IsPlaying = true; return Task.CompletedTask; }
        public void Pause() => IsPlaying = false; public void Resume() => IsPlaying = true;
        public void Seek(double milliseconds) { }
        public void Stop() { StopCalls++; IsPlaying = false; }
    }
}
