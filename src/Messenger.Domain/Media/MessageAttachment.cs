namespace Messenger.Domain.Media;

public sealed class MessageAttachment
{
    private MessageAttachment() { }
    public MessageAttachment(Guid messageId, Guid objectId, int position)
    {
        MessageId = messageId;
        ObjectId = objectId;
        Position = position;
    }

    public Guid MessageId { get; private set; }
    public Guid ObjectId { get; private set; }
    public int Position { get; private set; }
}
