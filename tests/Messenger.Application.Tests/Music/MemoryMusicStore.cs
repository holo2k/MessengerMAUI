using Messenger.Application.Music;
using Messenger.Domain.Music;

namespace Messenger.Application.Tests.Music;

internal sealed class MemoryMusicStore : IMusicStore
{
    public List<MusicTrack> Tracks { get; } = [];
    public List<UserMusicTrack> Library { get; } = [];
    public List<CopyrightClaim> Claims { get; } = [];
    public List<ModerationAction> Actions { get; } = [];
    public Task AddTrackAsync(MusicTrack track, CancellationToken ct) { Tracks.Add(track); return Task.CompletedTask; }
    public Task AddDeclarationAsync(RightsDeclaration declaration, CancellationToken ct) => Task.CompletedTask;
    public Task<MusicTrack?> FindTrackAsync(Guid id, CancellationToken ct) => Task.FromResult(Tracks.SingleOrDefault(x => x.Id == id));
    public Task<IReadOnlyList<MusicTrack>> SearchAsync(string query, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<MusicTrack>>(Tracks.Where(x => x.Status == MusicTrackStatus.Available && (x.Title + x.Artist).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray());
    public Task<IReadOnlyList<MusicTrack>> ListLibraryAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<MusicTrack>>((from item in Library join track in Tracks on item.TrackId equals track.Id where item.UserId == userId && track.Status == MusicTrackStatus.Available select track).ToArray());
    public Task<bool> LibraryContainsAsync(Guid userId, Guid trackId, CancellationToken ct) => Task.FromResult(Library.Any(x => x.UserId == userId && x.TrackId == trackId));
    public Task AddLibraryAsync(UserMusicTrack item, CancellationToken ct) { Library.Add(item); return Task.CompletedTask; }
    public Task RemoveLibraryAsync(Guid userId, Guid trackId, CancellationToken ct) { Library.RemoveAll(x => x.UserId == userId && x.TrackId == trackId); return Task.CompletedTask; }
    public Task AddClaimAsync(CopyrightClaim claim, CancellationToken ct) { Claims.Add(claim); return Task.CompletedTask; }
    public Task AddActionAsync(ModerationAction action, CancellationToken ct) { Actions.Add(action); return Task.CompletedTask; }
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
