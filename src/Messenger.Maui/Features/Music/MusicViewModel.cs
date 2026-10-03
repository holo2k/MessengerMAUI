using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Music;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Music;

public sealed class MusicViewModel(IMusicApi api, IMusicDownloadService downloads, IAudioPlayerService player, IAsyncDelay delay) : ObservableObject
{
    private CancellationTokenSource? _searchCancellation;
    private string _searchText = string.Empty, _status = string.Empty;
    private double _downloadProgress, _positionMs;
    private MusicTrackResponse? _currentTrack;
    public ObservableCollection<MusicTrackResponse> SearchResults { get; } = [];
    public ObservableCollection<MusicTrackResponse> Library { get; } = [];
    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public double DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, value); }
    public bool IsPlaying => player.IsPlaying;
    public string PositionText => TimeSpan.FromMilliseconds(_positionMs).ToString(@"mm\:ss");

    public async Task SearchAsync(CancellationToken ct = default)
    {
        _searchCancellation?.Cancel(); _searchCancellation?.Dispose();
        _searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct); var token = _searchCancellation.Token;
        try { await Task.Yield(); await delay.WaitAsync(TimeSpan.FromMilliseconds(300), token); var rows = await api.SearchAsync(SearchText, token); SearchResults.Clear(); foreach (var row in rows) SearchResults.Add(row); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async Task LoadLibraryAsync(CancellationToken ct = default) { Library.Clear(); foreach (var row in await api.LibraryAsync(ct)) Library.Add(row); }
    public async Task AddAsync(MusicTrackResponse track, CancellationToken ct = default) { await api.AddAsync(track.Id, ct); if (Library.All(x => x.Id != track.Id)) Library.Add(track); }
    public async Task RemoveAsync(MusicTrackResponse track, CancellationToken ct = default) { await api.RemoveAsync(track.Id, ct); var item = Library.FirstOrDefault(x => x.Id == track.Id); if (item is not null) Library.Remove(item); }
    public async Task UploadAsync(Guid audioId, string title, string artist, long duration, Guid? cover, bool rightsConfirmed, CancellationToken ct = default)
    { if (!rightsConfirmed) throw new InvalidOperationException("Нужно подтвердить наличие прав на трек."); var track = await api.CreateAsync(audioId, title, artist, duration, cover, ct); await api.DeclareAsync(track.Id, "Подтверждаю, что обладаю необходимыми правами на публикацию трека.", ct); Status = "Трек отправлен на модерацию"; }
    public async Task DownloadAsync(MusicTrackResponse track, CancellationToken ct = default) { DownloadProgress = 0; await downloads.DownloadAsync(track, new InlineProgress(x => DownloadProgress = x), ct); }
    public Task RemoveDownloadAsync(MusicTrackResponse track) => downloads.RemoveAsync(track.Id);
    public async Task PlayAsync(MusicTrackResponse track, CancellationToken ct = default) { var auth = await api.AuthorizeAsync(track.Id, false, ct); await player.PlayAsync(auth.Url, ct); _currentTrack = track; OnPropertyChanged(nameof(IsPlaying)); }
    public void TogglePause() { if (player.IsPlaying) player.Pause(); else player.Resume(); OnPropertyChanged(nameof(IsPlaying)); }
    public void Seek(double milliseconds) { _positionMs = milliseconds; player.Seek(milliseconds); OnPropertyChanged(nameof(PositionText)); }
    public async Task OnTrackBlockedAsync(Guid trackId, string reason) { if (_currentTrack?.Id == trackId) { player.Stop(); _currentTrack = null; OnPropertyChanged(nameof(IsPlaying)); } await downloads.InvalidateAsync(trackId); Status = reason; }
    private sealed class InlineProgress(Action<double> action) : IProgress<double> { public void Report(double value) => action(value); }
}
