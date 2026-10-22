using Messenger.Contracts.Music;
using Messenger.Contracts.Media;

namespace Messenger.Contracts.Messages;

public sealed record SendMessageRequest(Guid ClientMessageId, string Type, string? Body, IReadOnlyList<Guid>? AttachmentObjectIds = null, Guid? MusicTrackId = null);
public sealed record EditMessageRequest(string Body);
public sealed record ForwardMessageRequest(Guid ChatId, Guid ClientMessageId);
public sealed record MessageResponse(
    Guid Id,
    Guid ChatId,
    Guid SenderUserId,
    long Sequence,
    Guid ClientMessageId,
    string Type,
    string? Body,
    bool IsPinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    MusicTrackResponse? MusicTrack = null,
    IReadOnlyList<StoredObjectResponse>? Attachments = null,
    bool IsForwarded = false);
public sealed record MessagePageResponse(IReadOnlyList<MessageResponse> Items, string? NextCursor);
