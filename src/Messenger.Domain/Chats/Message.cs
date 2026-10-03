using Messenger.Domain.Common;

namespace Messenger.Domain.Chats;

public enum MessageKind
{
    Text = 0,
    Image = 1,
    Video = 2,
    Audio = 3,
    File = 4
}

public enum MessageRuleError
{
    NotFound,
    Forbidden,
    DeleteWindowExpired,
    InvalidBody
}

public sealed class MessageRuleException(MessageRuleError code) : Exception(code.ToString())
{
    public MessageRuleError Code { get; } = code;
}

public sealed class Message : Entity
{
    private Message()
    {
    }

    public Message(
        Guid id,
        Guid chatId,
        Guid senderUserId,
        long sequence,
        Guid clientMessageId,
        MessageKind kind,
        string bodyCiphertext,
        string bodyNonce,
        string bodyTag,
        int bodyKeyVersion,
        DateTimeOffset createdAt)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
        Id = id;
        ChatId = chatId;
        SenderUserId = senderUserId;
        Sequence = sequence;
        ClientMessageId = clientMessageId;
        Kind = kind;
        BodyCiphertext = bodyCiphertext;
        BodyNonce = bodyNonce;
        BodyTag = bodyTag;
        BodyKeyVersion = bodyKeyVersion;
        CreatedAt = createdAt;
    }

    public Guid ChatId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public long Sequence { get; private set; }
    public Guid ClientMessageId { get; private set; }
    public MessageKind Kind { get; private set; }
    public string BodyCiphertext { get; private set; } = string.Empty;
    public string BodyNonce { get; private set; } = string.Empty;
    public string BodyTag { get; private set; } = string.Empty;
    public int BodyKeyVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByUserId { get; private set; }

    public void Edit(
        Guid requesterUserId,
        string ciphertext,
        string nonce,
        string tag,
        int keyVersion,
        DateTimeOffset now)
    {
        if (requesterUserId != SenderUserId || DeletedAt is not null)
        {
            throw new MessageRuleException(MessageRuleError.Forbidden);
        }
        BodyCiphertext = ciphertext;
        BodyNonce = nonce;
        BodyTag = tag;
        BodyKeyVersion = keyVersion;
        EditedAt = now;
    }

    public void DeleteGlobally(Guid requesterUserId, DateTimeOffset now)
    {
        if (requesterUserId != SenderUserId)
        {
            throw new MessageRuleException(MessageRuleError.Forbidden);
        }
        if (now > CreatedAt.AddHours(48))
        {
            throw new MessageRuleException(MessageRuleError.DeleteWindowExpired);
        }
        DeletedAt ??= now;
        DeletedByUserId ??= requesterUserId;
    }
}

public sealed class MessageSearchToken
{
    private MessageSearchToken()
    {
    }

    public MessageSearchToken(Guid messageId, string token)
    {
        MessageId = messageId;
        Token = token;
    }

    public Guid MessageId { get; private set; }
    public string Token { get; private set; } = string.Empty;
}
