namespace Messenger.Contracts.Music;

public sealed record CreateTrackRequest(Guid AudioObjectId, string Title, string Artist, long DurationMs, Guid? CoverObjectId);
public sealed record RightsDeclarationRequest(string Statement);
public sealed record CopyrightClaimRequest(string Details);
public sealed record ModerateTrackRequest(string Action, string Reason);
public sealed record MusicTrackResponse(Guid Id, string Title, string Artist, long DurationMs, Guid? CoverObjectId, string Status);
