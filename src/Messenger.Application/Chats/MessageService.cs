using System.Text.RegularExpressions;
using Messenger.Application.Security;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Application.Music;
using Messenger.Domain.Music;

namespace Messenger.Application.Chats;

public sealed partial class MessageService(
    IMessageStore messageStore,
    IChatStore chatStore,
    IFieldCipher cipher,
    IBlindIndex blindIndex,
    IClock clock,
    IMusicStore music) : IMessageService, IMessageSearchService
{
    public Task<MessageView> SendMusicAsync(Guid senderUserId, Guid chatId, Guid clientMessageId, Guid trackId,
        CancellationToken cancellationToken = default) =>
        SendCoreAsync(senderUserId, chatId, clientMessageId, MessageKind.Music, null, trackId, cancellationToken);

    public Task<MessageView> SendAsync(
        Guid senderUserId,
        Guid chatId,
        Guid clientMessageId,
        MessageKind kind,
        string? body,
        CancellationToken cancellationToken = default) =>
        SendCoreAsync(senderUserId, chatId, clientMessageId, kind, body, null, cancellationToken);

    private async Task<MessageView> SendCoreAsync(Guid senderUserId, Guid chatId, Guid clientMessageId,
        MessageKind kind, string? body, Guid? trackId, CancellationToken cancellationToken)
    {
        await RequireMemberAsync(chatId, senderUserId, cancellationToken);
        var existing = await messageStore.FindByClientIdAsync(senderUserId, clientMessageId, cancellationToken);
        if (existing is not null)
        {
            if (existing.ChatId != chatId) throw new MessageRuleException(MessageRuleError.InvalidBody);
            return await ToViewAsync(existing, cancellationToken);
        }
        if (kind == MessageKind.Text && string.IsNullOrWhiteSpace(body))
        {
            throw new MessageRuleException(MessageRuleError.InvalidBody);
        }

        MusicTrack? track = null;
        if (kind == MessageKind.Music)
        {
            if (trackId is null) throw new MessageRuleException(MessageRuleError.InvalidBody);
            track = await music.FindTrackAsync(trackId.Value, cancellationToken);
            if (track is null || track.Status != MusicTrackStatus.Available)
                throw new MessageRuleException(MessageRuleError.NotFound);
            body = $"{track.Title} — {track.Artist}";
        }

        var chat = await chatStore.FindChatAsync(chatId, cancellationToken)
            ?? throw new MessageRuleException(MessageRuleError.NotFound);
        var normalizedBody = body ?? string.Empty;
        var encrypted = cipher.Encrypt(normalizedBody);
        var message = new Message(
            Guid.NewGuid(), chatId, senderUserId, chat.NextMessageSequence(clock.UtcNow), clientMessageId,
            kind, encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, encrypted.KeyVersion, clock.UtcNow);
        if (track is not null) message.AttachMusic(track.Id);
        await messageStore.AddMessageAsync(message, SearchTokens(message.Id, normalizedBody), cancellationToken);
        if (chat.Type == ChatType.Direct)
        {
            foreach (var member in await chatStore.ListMembersAsync(chatId, cancellationToken))
            {
                if (member.UserId != senderUserId && member.IsActive)
                {
                    member.Reactivate();
                }
            }
        }
        await chatStore.SaveChangesAsync(cancellationToken);
        return new MessageView(message, normalizedBody, false, track);
    }

    public async Task<IReadOnlyList<MessageView>> ListAsync(
        Guid requesterUserId,
        Guid chatId,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(chatId, requesterUserId, cancellationToken);
        var messages = await messageStore.ListMessagesAsync(
            chatId, requesterUserId, Math.Max(0, afterSequence), Math.Clamp(limit, 1, 100), cancellationToken);
        return await ToViewsAsync(messages, cancellationToken);
    }

    public async Task<MessageView> EditAsync(
        Guid requesterUserId,
        Guid messageId,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new MessageRuleException(MessageRuleError.InvalidBody);
        }
        var message = await RequireMessageForMemberAsync(requesterUserId, messageId, cancellationToken);
        if (message.Kind == MessageKind.Music) throw new MessageRuleException(MessageRuleError.InvalidBody);
        var encrypted = cipher.Encrypt(body);
        message.Edit(requesterUserId, encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, encrypted.KeyVersion, clock.UtcNow);
        await messageStore.ReplaceSearchTokensAsync(message.Id, SearchTokens(message.Id, body), cancellationToken);
        await chatStore.SaveChangesAsync(cancellationToken);
        return await ToViewAsync(message, cancellationToken);
    }

    public async Task DeleteAsync(
        Guid requesterUserId,
        Guid messageId,
        bool globally,
        CancellationToken cancellationToken = default)
    {
        if (!globally)
        {
            await HideAsync(requesterUserId, messageId, cancellationToken);
            return;
        }
        var message = await RequireMessageForMemberAsync(requesterUserId, messageId, cancellationToken);
        message.DeleteGlobally(requesterUserId, clock.UtcNow);
        await chatStore.SaveChangesAsync(cancellationToken);
    }

    public async Task HideAsync(Guid requesterUserId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await RequireMessageForMemberAsync(requesterUserId, messageId, cancellationToken);
        await messageStore.AddHiddenAsync(new HiddenMessage(message.Id, requesterUserId, clock.UtcNow), cancellationToken);
        await chatStore.SaveChangesAsync(cancellationToken);
    }

    public async Task PinAsync(Guid requesterUserId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var (message, chat, member) = await RequireMessageContextAsync(requesterUserId, messageId, cancellationToken);
        EnsureCanPin(chat, member);
        if (await messageStore.FindPinAsync(chat.Id, message.Id, cancellationToken) is null)
        {
            await messageStore.AddPinAsync(new PinnedMessage(chat.Id, message.Id, requesterUserId, clock.UtcNow), cancellationToken);
            await chatStore.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UnpinAsync(Guid requesterUserId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var (message, chat, member) = await RequireMessageContextAsync(requesterUserId, messageId, cancellationToken);
        EnsureCanPin(chat, member);
        var pin = await messageStore.FindPinAsync(chat.Id, message.Id, cancellationToken);
        if (pin is not null)
        {
            messageStore.RemovePin(pin);
            await chatStore.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MessageView>> ListPinsAsync(
        Guid requesterUserId,
        Guid chatId,
        CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(chatId, requesterUserId, cancellationToken);
        var pins = await messageStore.ListPinsAsync(chatId, cancellationToken);
        var messages = new List<Message>();
        foreach (var pin in pins)
        {
            var message = await messageStore.FindMessageAsync(pin.MessageId, cancellationToken);
            if (message is not null)
            {
                messages.Add(message);
            }
        }
        return await ToViewsAsync(messages, cancellationToken);
    }

    public async Task<IReadOnlyList<MessageView>> SearchAsync(
        Guid requesterUserId,
        Guid chatId,
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(chatId, requesterUserId, cancellationToken);
        var tokens = NormalizeWords(query).Select(blindIndex.Compute).ToArray();
        if (tokens.Length == 0)
        {
            return [];
        }
        var messages = await messageStore.SearchMessagesAsync(
            chatId, requesterUserId, tokens, Math.Clamp(limit, 1, 100), cancellationToken);
        return await ToViewsAsync(messages, cancellationToken);
    }

    private async Task<ChatMember> RequireMemberAsync(Guid chatId, Guid userId, CancellationToken cancellationToken)
    {
        var chat = await chatStore.FindChatAsync(chatId, cancellationToken);
        var member = await chatStore.FindMemberAsync(chatId, userId, cancellationToken);
        if (chat is null || chat.DeletedAt is not null || member is null || !member.IsActive)
        {
            throw new MessageRuleException(MessageRuleError.NotFound);
        }
        return member;
    }

    private async Task<Message> RequireMessageForMemberAsync(Guid userId, Guid messageId, CancellationToken cancellationToken)
    {
        var message = await messageStore.FindMessageAsync(messageId, cancellationToken)
            ?? throw new MessageRuleException(MessageRuleError.NotFound);
        await RequireMemberAsync(message.ChatId, userId, cancellationToken);
        return message;
    }

    private async Task<(Message Message, Chat Chat, ChatMember Member)> RequireMessageContextAsync(
        Guid userId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var message = await messageStore.FindMessageAsync(messageId, cancellationToken)
            ?? throw new MessageRuleException(MessageRuleError.NotFound);
        var member = await RequireMemberAsync(message.ChatId, userId, cancellationToken);
        var chat = await chatStore.FindChatAsync(message.ChatId, cancellationToken)
            ?? throw new MessageRuleException(MessageRuleError.NotFound);
        return (message, chat, member);
    }

    private static void EnsureCanPin(Chat chat, ChatMember member)
    {
        if (chat.Type == ChatType.Group && member.Role == ChatMemberRole.Member)
        {
            throw new MessageRuleException(MessageRuleError.Forbidden);
        }
    }

    private async Task<MessageView> ToViewAsync(Message message, CancellationToken cancellationToken)
    {
        var pinned = await messageStore.FindPinAsync(message.ChatId, message.Id, cancellationToken) is not null;
        var body = message.DeletedAt is null
            ? cipher.Decrypt(new EncryptedValue(
                message.BodyCiphertext, message.BodyNonce, message.BodyTag, message.BodyKeyVersion))
            : null;
        var track = message.DeletedAt is null && message.MusicTrackId is { } trackId
            ? await music.FindTrackAsync(trackId, cancellationToken) : null;
        return new MessageView(message, body, pinned, track?.Status == MusicTrackStatus.Available ? track : null);
    }

    private async Task<IReadOnlyList<MessageView>> ToViewsAsync(
        IEnumerable<Message> messages,
        CancellationToken cancellationToken)
    {
        var result = new List<MessageView>();
        foreach (var message in messages)
        {
            result.Add(await ToViewAsync(message, cancellationToken));
        }
        return result;
    }

    private IReadOnlyCollection<MessageSearchToken> SearchTokens(Guid messageId, string body) =>
        NormalizeWords(body).Select(word => new MessageSearchToken(messageId, blindIndex.Compute(word))).ToArray();

    private static IReadOnlyList<string> NormalizeWords(string value) => WordRegex().Matches(value.ToLowerInvariant())
        .Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();

    [GeneratedRegex(@"[\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
