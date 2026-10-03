namespace Messenger.Domain.Chats;

public enum ChatMemberRole
{
    Member = 0,
    Admin = 1,
    Owner = 2
}

public sealed class ChatMember
{
    private ChatMember()
    {
    }

    public ChatMember(Guid chatId, Guid userId, ChatMemberRole role, DateTimeOffset joinedAt)
    {
        ChatId = chatId;
        UserId = userId;
        Role = role;
        JoinedAt = joinedAt;
    }

    public Guid ChatId { get; private set; }
    public Guid UserId { get; private set; }
    public ChatMemberRole Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? LeftAt { get; private set; }
    public bool IsMuted { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset? HiddenAt { get; private set; }
    public long LastReadSequence { get; private set; }
    public bool IsActive => LeftAt is null;

    public void ChangeRole(
        ChatMember actor,
        ChatMemberRole role,
        IReadOnlyCollection<ChatMember> members)
    {
        EnsureSameActiveChat(actor);
        if (actor.Role != ChatMemberRole.Owner)
        {
            throw new ChatRuleException(ChatRuleError.Forbidden);
        }
        if (Role == ChatMemberRole.Owner && role != ChatMemberRole.Owner && ActiveOwnerCount(members) == 1)
        {
            throw new ChatRuleException(ChatRuleError.LastOwner);
        }
        Role = role;
    }

    public void Remove(
        ChatMember actor,
        IReadOnlyCollection<ChatMember> members,
        DateTimeOffset now)
    {
        EnsureSameActiveChat(actor);
        var allowed = actor.Role == ChatMemberRole.Owner ||
            actor.Role == ChatMemberRole.Admin && Role == ChatMemberRole.Member;
        if (!allowed)
        {
            throw new ChatRuleException(ChatRuleError.Forbidden);
        }
        if (Role == ChatMemberRole.Owner && ActiveOwnerCount(members) == 1)
        {
            throw new ChatRuleException(ChatRuleError.LastOwner);
        }
        LeftAt = now;
    }

    public void Leave(IReadOnlyCollection<ChatMember> members, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }
        if (Role == ChatMemberRole.Owner && ActiveOwnerCount(members) == 1)
        {
            throw new ChatRuleException(ChatRuleError.LastOwner);
        }
        LeftAt = now;
    }

    public void SetMuted(bool muted) => IsMuted = muted;
    public void SetArchived(bool archived) => IsArchived = archived;
    public void Hide(DateTimeOffset now) => HiddenAt = now;
    public void Reactivate() => HiddenAt = null;
    public void MarkRead(long sequence) => LastReadSequence = Math.Max(LastReadSequence, sequence);

    private void EnsureSameActiveChat(ChatMember actor)
    {
        if (ChatId != actor.ChatId || !IsActive || !actor.IsActive)
        {
            throw new ChatRuleException(ChatRuleError.InvalidMember);
        }
    }

    private static int ActiveOwnerCount(IEnumerable<ChatMember> members) =>
        members.Count(member => member.IsActive && member.Role == ChatMemberRole.Owner);
}
