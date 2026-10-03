using Messenger.Application.Media;
using Messenger.Domain.Common;
using Messenger.Domain.Music;

namespace Messenger.Application.Music;

public sealed class MusicService(IMusicStore store, IUploadService uploads, IClock clock) : IMusicService
{
    public async Task<MusicTrack> CreateAsync(Guid actorId, Guid audioObjectId, string title, string artist,
        long durationMs, Guid? coverObjectId, CancellationToken ct = default)
    {
        await uploads.ValidateOwnedAvailableAsync(actorId, audioObjectId, ct);
        if (coverObjectId is { } cover) await uploads.ValidateOwnedAvailableAsync(actorId, cover, ct);
        var track = new MusicTrack(Guid.NewGuid(), actorId, audioObjectId, title, artist, durationMs, coverObjectId, clock.UtcNow);
        await store.AddTrackAsync(track, ct);
        await store.SaveChangesAsync(ct);
        return track;
    }

    public async Task DeclareRightsAsync(Guid actorId, Guid trackId, string statement, CancellationToken ct = default)
    {
        var track = await RequireAsync(trackId, ct);
        var declaration = track.DeclareRights(actorId, statement, clock.UtcNow);
        await store.AddDeclarationAsync(declaration, ct);
        await store.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<MusicTrack>> SearchAsync(Guid actorId, string query, CancellationToken ct = default) =>
        store.SearchAsync(query.Trim(), 100, ct);
    public Task<IReadOnlyList<MusicTrack>> ListLibraryAsync(Guid actorId, CancellationToken ct = default) => store.ListLibraryAsync(actorId, ct);

    public async Task AddToLibraryAsync(Guid actorId, Guid trackId, CancellationToken ct = default)
    {
        var track = await RequireAvailableAsync(trackId, ct);
        if (!await store.LibraryContainsAsync(actorId, track.Id, ct))
        {
            await store.AddLibraryAsync(new UserMusicTrack(actorId, track.Id, clock.UtcNow), ct);
            await store.SaveChangesAsync(ct);
        }
    }

    public async Task RemoveFromLibraryAsync(Guid actorId, Guid trackId, CancellationToken ct = default)
    {
        await store.RemoveLibraryAsync(actorId, trackId, ct);
        await store.SaveChangesAsync(ct);
    }

    public async Task<DownloadAuthorization> AuthorizeStreamAsync(Guid actorId, Guid trackId, CancellationToken ct = default)
    {
        var track = await RequireAvailableAsync(trackId, ct);
        return await uploads.AuthorizeDownloadAsync(track.UploaderUserId, track.AudioObjectId, ct);
    }

    public async Task SubmitClaimAsync(Guid actorId, Guid trackId, string details, CancellationToken ct = default)
    {
        await RequireAsync(trackId, ct);
        if (string.IsNullOrWhiteSpace(details)) throw new MusicRuleException(MusicRuleError.InvalidState);
        await store.AddClaimAsync(new CopyrightClaim(Guid.NewGuid(), trackId, actorId, details.Trim(), clock.UtcNow), ct);
        await store.SaveChangesAsync(ct);
    }

    private async Task<MusicTrack> RequireAsync(Guid id, CancellationToken ct) =>
        await store.FindTrackAsync(id, ct) ?? throw new MusicRuleException(MusicRuleError.NotFound);
    private async Task<MusicTrack> RequireAvailableAsync(Guid id, CancellationToken ct)
    {
        var track = await RequireAsync(id, ct);
        if (track.Status != MusicTrackStatus.Available) throw new MusicRuleException(MusicRuleError.NotFound);
        return track;
    }
}

public sealed class MusicModerationService(IMusicStore store, IClock clock, MusicAdminOptions options) : IMusicModerationService
{
    public async Task DecideAsync(Guid administratorId, Guid trackId, string action, string reason, CancellationToken ct = default)
    {
        if (!options.UserIds.Contains(administratorId)) throw new MusicRuleException(MusicRuleError.Forbidden);
        var track = await store.FindTrackAsync(trackId, ct) ?? throw new MusicRuleException(MusicRuleError.NotFound);
        switch (action.ToLowerInvariant())
        {
            case "approve": track.Approve(clock.UtcNow); break;
            case "block": track.Block(clock.UtcNow); break;
            case "delete": track.Delete(clock.UtcNow); break;
            default: throw new MusicRuleException(MusicRuleError.InvalidState);
        }
        await store.AddActionAsync(new ModerationAction(Guid.NewGuid(), trackId, administratorId,
            action.ToLowerInvariant(), reason.Trim(), clock.UtcNow), ct);
        await store.SaveChangesAsync(ct);
    }
}
