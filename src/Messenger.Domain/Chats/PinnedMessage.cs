namespace Messenger.Domain.Chats;

public sealed class PinnedMessage
{
    private PinnedMessage()
    {
    }

    public PinnedMessage(Guid chatId, Guid messageId, Guid pinnedByUserId, DateTimeOffset pinnedAt)
    {
        ChatId = chatId;
        MessageId = messageId;
        PinnedByUserId = pinnedByUserId;
        PinnedAt = pinnedAt;
    }

    public Guid ChatId { get; private set; }
    public Guid MessageId { get; private set; }
    public Guid PinnedByUserId { get; private set; }
    public DateTimeOffset PinnedAt { get; private set; }
}
