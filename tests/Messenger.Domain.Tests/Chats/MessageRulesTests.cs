using Messenger.Domain.Chats;

namespace Messenger.Domain.Tests.Chats;

public sealed class MessageRulesTests
{
    [Theory]
    [InlineData(MessageKind.Voice, 600000)]
    [InlineData(MessageKind.VideoNote, 60000)]
    public void Recording_metadata_is_bounded_and_copied_without_reusing_mutable_waveform_arrays(MessageKind kind, long maximum)
    {
        var message = new Message(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), kind, "body", "nonce", "tag", 1, Now);
        byte[] waveform = [0, 50, 100];
        message.SetRecordingMetadata(maximum, waveform);
        waveform[1] = 0;
        Assert.Equal(50, message.Waveform![1]);
        Assert.Equal(maximum, message.DurationMs);
        Assert.Throws<MessageRuleException>(() => message.SetRecordingMetadata(maximum + 1, []));
        Assert.Throws<MessageRuleException>(() => message.SetRecordingMetadata(100, []));
        Assert.Throws<MessageRuleException>(() => message.SetRecordingMetadata(1000, new byte[65]));
        Assert.Throws<MessageRuleException>(() => message.SetRecordingMetadata(1000, [101]));
    }
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Chat_message_sequences_are_strictly_monotonic()
    {
        var chat = new Chat(Guid.NewGuid(), ChatType.Group, Guid.NewGuid(), "Группа", Now);

        var first = chat.NextMessageSequence(Now.AddSeconds(1));
        var second = chat.NextMessageSequence(Now.AddSeconds(2));

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(2, chat.CurrentMessageSequence);
    }

    [Fact]
    public void Only_sender_can_edit_a_message()
    {
        var senderId = Guid.NewGuid();
        var message = Message(senderId);

        var error = Assert.Throws<MessageRuleException>(() => message.Edit(
            Guid.NewGuid(), "changed", "nonce", "tag", 1, Now.AddMinutes(1)));

        Assert.Equal(MessageRuleError.Forbidden, error.Code);
    }

    [Fact]
    public void Sender_can_delete_globally_without_a_time_limit_but_other_users_cannot()
    {
        var senderId = Guid.NewGuid();
        var recent = Message(senderId);
        var old = Message(senderId);

        recent.DeleteGlobally(senderId, Now.AddHours(48));
        old.DeleteGlobally(senderId, Now.AddYears(2));
        var error = Assert.Throws<MessageRuleException>(() =>
            Message(senderId).DeleteGlobally(Guid.NewGuid(), Now.AddYears(2)));

        Assert.NotNull(recent.DeletedAt);
        Assert.NotNull(old.DeletedAt);
        Assert.Equal(MessageRuleError.Forbidden, error.Code);
    }

    [Fact]
    public void Hiding_a_message_is_scoped_to_one_user()
    {
        var message = Message(Guid.NewGuid());
        var userId = Guid.NewGuid();

        var hidden = new HiddenMessage(message.Id, userId, Now);

        Assert.Equal(message.Id, hidden.MessageId);
        Assert.Equal(userId, hidden.UserId);
        Assert.Null(message.DeletedAt);
    }

    private static Message Message(Guid senderId) => new(
        Guid.NewGuid(), Guid.NewGuid(), senderId, 1, Guid.NewGuid(), MessageKind.Text,
        "ciphertext", "nonce", "tag", 1, Now);
}
