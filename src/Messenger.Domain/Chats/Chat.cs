using Messenger.Domain.Common;

namespace Messenger.Domain.Chats;

public enum ChatType
{
    Direct = 0,
    Group = 1
}

public sealed class Chat : Entity
{
    private Chat()
    {
    }

    public Chat(
        Guid id,
        ChatType type,
        Guid createdByUserId,
        string? title,
        DateTimeOffset createdAt)
    {
        if (type == ChatType.Group && string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A group title is required.", nameof(title));
        }

        Id = id;
        Type = type;
        CreatedByUserId = createdByUserId;
        Title = type == ChatType.Group ? title!.Trim() : null;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public ChatType Type { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string? Title { get; private set; }
    public string? AvatarObjectId { get; private set; }
    public long CurrentMessageSequence { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void UpdateGroup(string title, string? avatarObjectId, DateTimeOffset now)
    {
        if (Type != ChatType.Group)
        {
            throw new ChatRuleException(ChatRuleError.InvalidChatType);
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A group title is required.", nameof(title));
        }
        Title = title.Trim();
        AvatarObjectId = string.IsNullOrWhiteSpace(avatarObjectId) ? null : avatarObjectId;
        UpdatedAt = now;
    }

    public void Delete(DateTimeOffset now)
    {
        DeletedAt ??= now;
        UpdatedAt = now;
    }

    public long NextMessageSequence(DateTimeOffset now)
    {
        CurrentMessageSequence++;
        UpdatedAt = now;
        return CurrentMessageSequence;
    }
}

public sealed class DirectChatPair
{
    private DirectChatPair()
    {
    }

    private DirectChatPair(Guid chatId, Guid lowerUserId, Guid higherUserId)
    {
        ChatId = chatId;
        LowerUserId = lowerUserId;
        HigherUserId = higherUserId;
    }

    public Guid ChatId { get; private set; }
    public Guid LowerUserId { get; private set; }
    public Guid HigherUserId { get; private set; }

    public static DirectChatPair Create(Guid chatId, Guid firstUserId, Guid secondUserId)
    {
        if (firstUserId == secondUserId)
        {
            throw new ArgumentException("A direct chat requires two different users.");
        }
        return firstUserId.CompareTo(secondUserId) < 0
            ? new DirectChatPair(chatId, firstUserId, secondUserId)
            : new DirectChatPair(chatId, secondUserId, firstUserId);
    }
}

public enum ChatRuleError
{
    Forbidden,
    LastOwner,
    InvalidChatType,
    NotFound,
    AlreadyMember,
    InvalidMember
}

public sealed class ChatRuleException(ChatRuleError code) : Exception(code.ToString())
{
    public ChatRuleError Code { get; } = code;
}
