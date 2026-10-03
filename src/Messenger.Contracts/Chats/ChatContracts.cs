namespace Messenger.Contracts.Chats;

public sealed record CreateDirectChatRequest(Guid TargetUserId);
public sealed record CreateGroupChatRequest(string Title, IReadOnlyList<Guid> MemberUserIds);
public sealed record UpdateGroupChatRequest(string Title, string? AvatarObjectId);
public sealed record ChatResponse(
    Guid Id,
    string Type,
    string? Title,
    string? AvatarObjectId,
    string Role,
    bool IsMuted,
    bool IsArchived,
    bool IsHidden,
    long CurrentMessageSequence,
    DateTimeOffset UpdatedAt);
public sealed record ChatPageResponse(IReadOnlyList<ChatResponse> Items, string? NextCursor);
public sealed record ChatMemberResponse(Guid UserId, string Role, DateTimeOffset JoinedAt);
public sealed record AddChatMemberRequest(Guid UserId);
public sealed record UpdateChatMemberRequest(string Role);
public sealed record ChatFlagRequest(bool Value);
public sealed record ReadChatRequest(long Sequence);
public sealed record CreateChatFolderRequest(string Title);
public sealed record UpdateChatFolderRequest(string Title);
public sealed record ReorderChatFoldersRequest(IReadOnlyList<Guid> FolderIds);
public sealed record SetChatFolderChatsRequest(IReadOnlyList<Guid> ChatIds);
public sealed record ChatFolderResponse(Guid Id, string Title, int Position, IReadOnlyList<Guid> ChatIds);
