using System.Net.Http.Json;
using Messenger.Contracts.Media;
using Messenger.Contracts.Music;
using Messenger.Contracts.Realtime;
using Messenger.Contracts.Messages;

#if ANDROID || IOS || MACCATALYST || WINDOWS
using Plugin.Maui.Audio;
#endif

namespace Messenger.Maui.Services;

public interface IAsyncDelay { Task WaitAsync(TimeSpan delay, CancellationToken ct); }
public sealed class AsyncDelay : IAsyncDelay { public Task WaitAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct); }
public interface IMusicRealtimeClient
{
    event Func<MusicTrackStatusChangedEvent, Task>? TrackStatusChanged;
    event Func<MusicTrackUpdatedEvent, Task>? TrackUpdated;
    Task ConnectAsync(CancellationToken ct = default);
}

public interface IMusicApi
{
    Task<IReadOnlyList<MusicTrackResponse>> SearchAsync(string query, CancellationToken ct);
    Task<IReadOnlyList<MusicTrackResponse>> LibraryAsync(CancellationToken ct);
    Task AddAsync(Guid id, CancellationToken ct);
    Task RemoveAsync(Guid id, CancellationToken ct);
    Task<MusicTrackResponse> CreateAsync(Guid audio, string title, string artist, long duration, Guid? cover, CancellationToken ct);
    Task DeclareAsync(Guid id, string statement, CancellationToken ct);
    Task<MusicTrackResponse> UpdateCoverAsync(Guid id, Guid coverObjectId, CancellationToken ct);
    Task<MessageResponse> ShareAsync(Guid id, Guid chatId, Guid clientMessageId, CancellationToken ct);
    Task<DownloadAuthorizationResponse> AuthorizeAsync(Guid id, bool download, CancellationToken ct);
}

public sealed class MusicApiClient(HttpClient http) : IMusicApi
{
    public async Task<IReadOnlyList<MusicTrackResponse>> SearchAsync(string query, CancellationToken ct) => await http.GetFromJsonAsync<List<MusicTrackResponse>>($"api/music/search?query={Uri.EscapeDataString(query)}", ct) ?? [];
    public async Task<IReadOnlyList<MusicTrackResponse>> LibraryAsync(CancellationToken ct) => await http.GetFromJsonAsync<List<MusicTrackResponse>>("api/music/library", ct) ?? [];
    public Task AddAsync(Guid id, CancellationToken ct) => EnsureAsync(http.PutAsync($"api/music/library/{id}", null, ct));
    public Task RemoveAsync(Guid id, CancellationToken ct) => EnsureAsync(http.DeleteAsync($"api/music/library/{id}", ct));
    public async Task<MusicTrackResponse> CreateAsync(Guid audio, string title, string artist, long duration, Guid? cover, CancellationToken ct) => await PostAsync<MusicTrackResponse>("api/music/tracks", new CreateTrackRequest(audio, title, artist, duration, cover), ct);
    public Task DeclareAsync(Guid id, string statement, CancellationToken ct) => EnsureAsync(http.PostAsJsonAsync($"api/music/tracks/{id}/declaration", new RightsDeclarationRequest(statement), ct));
    public async Task<MusicTrackResponse> UpdateCoverAsync(Guid id, Guid coverObjectId, CancellationToken ct)
    {
        using var response = await http.PutAsJsonAsync($"api/music/tracks/{id}/cover", new UpdateTrackCoverRequest(coverObjectId), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MusicTrackResponse>(ct) ?? throw new InvalidDataException("Missing track response.");
    }
    public Task<MessageResponse> ShareAsync(Guid id, Guid chatId, Guid clientMessageId, CancellationToken ct) =>
        PostAsync<MessageResponse>($"api/chats/{chatId}/messages", new SendMessageRequest(clientMessageId, "music", null, MusicTrackId: id), ct);
    public async Task<DownloadAuthorizationResponse> AuthorizeAsync(Guid id, bool download, CancellationToken ct) => await http.GetFromJsonAsync<DownloadAuthorizationResponse>($"api/music/tracks/{id}/{(download ? "download" : "stream")}", ct) ?? throw new InvalidDataException("Missing music authorization.");
    private async Task<T> PostAsync<T>(string uri, object body, CancellationToken ct) { using var r = await http.PostAsJsonAsync(uri, body, ct); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<T>(ct))!; }
    private static async Task EnsureAsync(Task<HttpResponseMessage> task) { using var r = await task; r.EnsureSuccessStatusCode(); }
}

public interface IMusicDownloadService
{
    Task<string> DownloadAsync(MusicTrackResponse track, IProgress<double> progress, CancellationToken ct);
    Task RemoveAsync(Guid id);
    Task InvalidateAsync(Guid id);
}
public interface IAudioPlayerService
{
    bool IsPlaying { get; }
    double PositionMs { get; }
    double DurationMs { get; }
    bool IsBuffering { get; }
    double BufferedProgress { get; }
    string? PlaybackError { get; }
    event EventHandler? PlaybackEnded;
    Task PlayAsync(string source, CancellationToken ct);
    void Pause(); void Resume(); void Seek(double milliseconds); void Stop();
}

internal static class PlaybackTime
{
    public static string Format(double milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(double.IsFinite(milliseconds) ? Math.Max(0, milliseconds) : 0);
        return time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
    }
}

internal static class AudioSourceLoader
{
    public static async Task<Stream> OpenAsync(HttpClient transfer, string source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (File.Exists(source)) return File.OpenRead(source);

        // Native players read their input synchronously. Finish all network reads
        // asynchronously before handing the stream to the player on the UI thread.
        await using var remote = await transfer.GetStreamAsync(source, ct).ConfigureAwait(false);
        var buffered = new MemoryStream();
        try
        {
            await remote.CopyToAsync(buffered, ct).ConfigureAwait(false);
            buffered.Position = 0;
            return buffered;
        }
        catch
        {
            buffered.Dispose();
            throw;
        }
    }
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public sealed class MusicDownloadService(IMusicApi api, HttpClient transfer) : IMusicDownloadService
{
    public async Task<string> DownloadAsync(MusicTrackResponse track, IProgress<double> progress, CancellationToken ct)
    {
        var auth = await api.AuthorizeAsync(track.Id, true, ct);
        using var response = await transfer.GetAsync(auth.Url, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
        var path = Path.Combine(FileSystem.AppDataDirectory, $"music-{track.Id:N}.bin");
        await using var input = await response.Content.ReadAsStreamAsync(ct); await using var output = File.Create(path);
        var total = response.Content.Headers.ContentLength ?? 1; var buffer = new byte[64 * 1024]; long read = 0; int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0) { await output.WriteAsync(buffer.AsMemory(0, count), ct); read += count; progress.Report(Math.Min(1, (double)read / total)); }
        return path;
    }
    public Task RemoveAsync(Guid id) { var path = Path.Combine(FileSystem.AppDataDirectory, $"music-{id:N}.bin"); if (File.Exists(path)) File.Delete(path); return Task.CompletedTask; }
    public Task InvalidateAsync(Guid id) => RemoveAsync(id);
}

public sealed class AudioPlayerService(IAudioManager audioManager, HttpClient transfer) : IAudioPlayerService
{
    private IAudioPlayer? _player; private Stream? _stream;
    public bool IsPlaying => _player?.IsPlaying == true;
    public double PositionMs => (_player?.CurrentPosition ?? 0) * 1000d;
    public double DurationMs => (_player?.Duration ?? 0) * 1000d;
    public bool IsBuffering { get; private set; }
    public double BufferedProgress => _player is null ? 0 : 1;
    public string? PlaybackError => null;
    public event EventHandler? PlaybackEnded;
    public async Task PlayAsync(string source, CancellationToken ct)
    {
        Stop();
        IsBuffering = true;
        try
        {
            _stream = await AudioSourceLoader.OpenAsync(transfer, source, ct);
            _player = audioManager.CreatePlayer(_stream);
            _player.PlaybackEnded += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
            _player.Play();
        }
        catch
        {
            Stop();
            throw;
        }
        finally { IsBuffering = false; }
    }
    public void Pause() => _player?.Pause(); public void Resume() => _player?.Play();
    public void Seek(double milliseconds) => _player?.Seek(milliseconds / 1000d);
    public void Stop() { _player?.Stop(); _player?.Dispose(); _stream?.Dispose(); _player = null; _stream = null; }
}
#endif
