using Messenger.Application.Music;
using Messenger.Domain.Music;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Music;

public sealed class EfMusicStore(MessengerDbContext db) : IMusicStore
{
    public Task AddTrackAsync(MusicTrack track, CancellationToken ct) => db.MusicTracks.AddAsync(track, ct).AsTask();
    public Task AddDeclarationAsync(RightsDeclaration declaration, CancellationToken ct) => db.RightsDeclarations.AddAsync(declaration, ct).AsTask();
    public Task<MusicTrack?> FindTrackAsync(Guid id, CancellationToken ct) => db.MusicTracks.Include(x => x.Declaration).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<MusicTrack>> SearchAsync(string query, int take, CancellationToken ct)
    {
        var normalized = query.ToLower();
        return await db.MusicTracks.Where(x => x.Status == MusicTrackStatus.Available &&
            (x.Title.ToLower().Contains(normalized) || x.Artist.ToLower().Contains(normalized)))
            .OrderBy(x => x.Artist).ThenBy(x => x.Title).Take(take).ToArrayAsync(ct);
    }
    public async Task<IReadOnlyList<MusicTrack>> ListLibraryAsync(Guid userId, CancellationToken ct) =>
        await (from item in db.UserMusicTracks join track in db.MusicTracks on item.TrackId equals track.Id
               where item.UserId == userId && track.Status == MusicTrackStatus.Available orderby item.AddedAt descending select track).ToArrayAsync(ct);
    public Task<bool> LibraryContainsAsync(Guid userId, Guid trackId, CancellationToken ct) => db.UserMusicTracks.AnyAsync(x => x.UserId == userId && x.TrackId == trackId, ct);
    public Task AddLibraryAsync(UserMusicTrack item, CancellationToken ct) => db.UserMusicTracks.AddAsync(item, ct).AsTask();
    public async Task RemoveLibraryAsync(Guid userId, Guid trackId, CancellationToken ct)
    {
        var value = await db.UserMusicTracks.SingleOrDefaultAsync(x => x.UserId == userId && x.TrackId == trackId, ct);
        if (value is not null) db.UserMusicTracks.Remove(value);
    }
    public Task AddClaimAsync(CopyrightClaim claim, CancellationToken ct) => db.CopyrightClaims.AddAsync(claim, ct).AsTask();
    public Task AddActionAsync(ModerationAction action, CancellationToken ct) => db.ModerationActions.AddAsync(action, ct).AsTask();
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
