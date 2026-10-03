using Messenger.Application.Media;
using Messenger.Domain.Music;

namespace Messenger.Application.Music;

public sealed class MusicAdminOptions { public HashSet<Guid> UserIds { get; set; } = []; }

public interface IMusicStore
{
    Task AddTrackAsync(MusicTrack track, CancellationToken ct);
    Task AddDeclarationAsync(RightsDeclaration declaration, CancellationToken ct);
    Task<MusicTrack?> FindTrackAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<MusicTrack>> SearchAsync(string query, int take, CancellationToken ct);
    Task<IReadOnlyList<MusicTrack>> ListLibraryAsync(Guid userId, CancellationToken ct);
    Task<bool> LibraryContainsAsync(Guid userId, Guid trackId, CancellationToken ct);
    Task AddLibraryAsync(UserMusicTrack item, CancellationToken ct);
    Task RemoveLibraryAsync(Guid userId, Guid trackId, CancellationToken ct);
    Task AddClaimAsync(CopyrightClaim claim, CancellationToken ct);
    Task AddActionAsync(ModerationAction action, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IMusicService
{
    Task<MusicTrack> CreateAsync(Guid actorId, Guid audioObjectId, string title, string artist, long durationMs, Guid? coverObjectId, CancellationToken ct = default);
    Task DeclareRightsAsync(Guid actorId, Guid trackId, string statement, CancellationToken ct = default);
    Task<IReadOnlyList<MusicTrack>> SearchAsync(Guid actorId, string query, CancellationToken ct = default);
    Task<IReadOnlyList<MusicTrack>> ListLibraryAsync(Guid actorId, CancellationToken ct = default);
    Task AddToLibraryAsync(Guid actorId, Guid trackId, CancellationToken ct = default);
    Task RemoveFromLibraryAsync(Guid actorId, Guid trackId, CancellationToken ct = default);
    Task<DownloadAuthorization> AuthorizeStreamAsync(Guid actorId, Guid trackId, CancellationToken ct = default);
    Task SubmitClaimAsync(Guid actorId, Guid trackId, string details, CancellationToken ct = default);
}

public interface IMusicModerationService
{
    Task DecideAsync(Guid administratorId, Guid trackId, string action, string reason, CancellationToken ct = default);
}
