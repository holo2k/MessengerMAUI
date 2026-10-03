using Messenger.Domain.Common;

namespace Messenger.Domain.Music;

public enum MusicTrackStatus { Pending, Available, Blocked, Deleted }
public enum MusicClaimStatus { Open, Accepted, Rejected }
public enum MusicRuleError { InvalidState, DeclarationRequired, AlreadyDeclared, Forbidden, NotFound, Duplicate }

public sealed class MusicRuleException(MusicRuleError code) : Exception(code.ToString())
{
    public MusicRuleError Code { get; } = code;
}

public sealed class MusicTrack : Entity
{
    private MusicTrack() { }
    public MusicTrack(Guid id, Guid uploaderUserId, Guid audioObjectId, string title, string artist,
        long durationMs, Guid? coverObjectId, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist) || durationMs <= 0)
            throw new MusicRuleException(MusicRuleError.InvalidState);
        Id = id;
        UploaderUserId = uploaderUserId;
        AudioObjectId = audioObjectId;
        Title = title.Trim();
        Artist = artist.Trim();
        DurationMs = durationMs;
        CoverObjectId = coverObjectId;
        CreatedAt = createdAt;
    }

    public Guid UploaderUserId { get; private set; }
    public Guid AudioObjectId { get; private set; }
    public Guid? CoverObjectId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Artist { get; private set; } = string.Empty;
    public long DurationMs { get; private set; }
    public MusicTrackStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public RightsDeclaration? Declaration { get; private set; }

    public RightsDeclaration DeclareRights(Guid userId, string statement, DateTimeOffset now)
    {
        if (userId != UploaderUserId) throw new MusicRuleException(MusicRuleError.Forbidden);
        if (Declaration is not null) throw new MusicRuleException(MusicRuleError.AlreadyDeclared);
        if (string.IsNullOrWhiteSpace(statement)) throw new MusicRuleException(MusicRuleError.InvalidState);
        Declaration = new RightsDeclaration(Guid.NewGuid(), Id, userId, statement.Trim(), now);
        return Declaration;
    }

    public void Approve(DateTimeOffset now)
    {
        if (Declaration is null) throw new MusicRuleException(MusicRuleError.DeclarationRequired);
        if (Status != MusicTrackStatus.Pending) throw new MusicRuleException(MusicRuleError.InvalidState);
        Status = MusicTrackStatus.Available; UpdatedAt = now;
    }
    public void Block(DateTimeOffset now) { if (Status == MusicTrackStatus.Deleted) throw new MusicRuleException(MusicRuleError.InvalidState); Status = MusicTrackStatus.Blocked; UpdatedAt = now; }
    public void Delete(DateTimeOffset now) { Status = MusicTrackStatus.Deleted; UpdatedAt = now; }
}

public sealed class RightsDeclaration : Entity
{
    private RightsDeclaration() { }
    public RightsDeclaration(Guid id, Guid trackId, Guid userId, string statement, DateTimeOffset createdAt)
    { Id = id; TrackId = trackId; UserId = userId; Statement = statement; CreatedAt = createdAt; }
    public Guid TrackId { get; private set; }
    public Guid UserId { get; private set; }
    public string Statement { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class UserMusicTrack
{
    private UserMusicTrack() { }
    public UserMusicTrack(Guid userId, Guid trackId, DateTimeOffset addedAt) { UserId = userId; TrackId = trackId; AddedAt = addedAt; }
    public Guid UserId { get; private set; }
    public Guid TrackId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
}

public sealed class CopyrightClaim : Entity
{
    private CopyrightClaim() { }
    public CopyrightClaim(Guid id, Guid trackId, Guid claimantUserId, string details, DateTimeOffset createdAt)
    { Id = id; TrackId = trackId; ClaimantUserId = claimantUserId; Details = details; CreatedAt = createdAt; }
    public Guid TrackId { get; private set; }
    public Guid ClaimantUserId { get; private set; }
    public string Details { get; private set; } = string.Empty;
    public MusicClaimStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class ModerationAction : Entity
{
    private ModerationAction() { }
    public ModerationAction(Guid id, Guid trackId, Guid administratorUserId, string action, string reason, DateTimeOffset createdAt)
    { Id = id; TrackId = trackId; AdministratorUserId = administratorUserId; Action = action; Reason = reason; CreatedAt = createdAt; }
    public Guid TrackId { get; private set; }
    public Guid AdministratorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
