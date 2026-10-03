namespace Messenger.Contracts.Realtime;

public sealed record MessageCreatedEvent(Guid ChatId, Guid MessageId, long Sequence);
public sealed record MessageUpdatedEvent(Guid ChatId, Guid MessageId, long Sequence);
public sealed record MessageDeletedEvent(Guid ChatId, Guid MessageId, long Sequence);
public sealed record ReadPositionChangedEvent(Guid ChatId, Guid UserId, long Sequence);
public sealed record ChatChangedEvent(Guid ChatId, long Cursor);
public sealed record MemberChangedEvent(Guid ChatId, Guid UserId, long Cursor);
public sealed record MusicTrackStatusChangedEvent(Guid TrackId, string Status, string Reason);
