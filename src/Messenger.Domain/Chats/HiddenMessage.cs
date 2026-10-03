namespace Messenger.Domain.Chats;

public sealed class HiddenMessage
{
    private HiddenMessage()
    {
    }

    public HiddenMessage(Guid messageId, Guid userId, DateTimeOffset hiddenAt)
    {
        MessageId = messageId;
        UserId = userId;
        HiddenAt = hiddenAt;
    }

    public Guid MessageId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset HiddenAt { get; private set; }
}
