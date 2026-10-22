using Messenger.Application.Chats;
using Messenger.Application.Security;
using Messenger.Domain.Chats;
using Messenger.Domain.Users;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Social;

public sealed class EfChatSummaryReader(
    MessengerDbContext dbContext,
    IFieldCipher cipher,
    ILogger<EfChatSummaryReader> logger) : IChatSummaryReader
{
    public async Task<ChatView?> GetAsync(
        Guid requesterUserId,
        Guid chatId,
        CancellationToken cancellationToken)
    {
        var rows = await (from member in dbContext.ChatMembers
            join chat in dbContext.Chats on member.ChatId equals chat.Id
            where member.UserId == requesterUserId && member.LeftAt == null &&
                  chat.DeletedAt == null && chat.Id == chatId
            select new SummaryRow(chat, member))
            .ToListAsync(cancellationToken);
        return (await EnrichAsync(requesterUserId, rows, cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<ChatView>> ListAsync(
        Guid requesterUserId,
        DateTimeOffset? afterUpdatedAt,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken)
    {
        var query = from member in dbContext.ChatMembers
            join chat in dbContext.Chats on member.ChatId equals chat.Id
            where member.UserId == requesterUserId && member.LeftAt == null &&
                  member.HiddenAt == null && chat.DeletedAt == null
            select new { Chat = chat, Member = member };
        if (afterUpdatedAt is not null && afterId is not null)
        {
            query = query.Where(row => row.Chat.UpdatedAt < afterUpdatedAt ||
                row.Chat.UpdatedAt == afterUpdatedAt && row.Chat.Id.CompareTo(afterId.Value) < 0);
        }

        var rows = await query.OrderByDescending(row => row.Chat.UpdatedAt)
            .ThenByDescending(row => row.Chat.Id)
            .Take(take)
            .Select(row => new SummaryRow(row.Chat, row.Member))
            .ToListAsync(cancellationToken);
        return await EnrichAsync(requesterUserId, rows, cancellationToken);
    }

    private async Task<IReadOnlyList<ChatView>> EnrichAsync(
        Guid requesterUserId,
        IReadOnlyList<SummaryRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var chatIds = rows.Select(row => row.Chat.Id).ToArray();
        var directChatIds = rows.Where(row => row.Chat.Type == ChatType.Direct)
            .Select(row => row.Chat.Id).ToArray();
        var peers = await dbContext.ChatMembers
            .Where(member => directChatIds.Contains(member.ChatId) && member.UserId != requesterUserId && member.LeftAt == null)
            .ToListAsync(cancellationToken);
        var peerByChat = peers.GroupBy(member => member.ChatId)
            .ToDictionary(group => group.Key, group => group.First());
        var peerIds = peers.Select(member => member.UserId).Distinct().ToArray();
        var profiles = await dbContext.UserProfiles.Where(profile => peerIds.Contains(profile.UserId))
            .ToDictionaryAsync(profile => profile.UserId, cancellationToken);
        var users = await dbContext.Users.Where(user => peerIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
        var contacts = await dbContext.Contacts
            .Where(contact => contact.OwnerUserId == requesterUserId && peerIds.Contains(contact.TargetUserId))
            .ToDictionaryAsync(contact => contact.TargetUserId, cancellationToken);
        var reverseContactIds = await dbContext.Contacts
            .Where(contact => peerIds.Contains(contact.OwnerUserId) && contact.TargetUserId == requesterUserId)
            .Select(contact => contact.OwnerUserId)
            .ToListAsync(cancellationToken);
        var reverseContacts = reverseContactIds.ToHashSet();
        var privacy = await dbContext.UserPrivacySettings.Where(value => peerIds.Contains(value.UserId))
            .ToDictionaryAsync(value => value.UserId, cancellationToken);
        var lastMessages = await dbContext.Messages
            .Where(message => chatIds.Contains(message.ChatId) && message.DeletedAt == null &&
                !dbContext.HiddenMessages.Any(hidden => hidden.MessageId == message.Id && hidden.UserId == requesterUserId))
            .GroupBy(message => message.ChatId)
            .Select(group => group.OrderByDescending(message => message.Sequence).First())
            .ToListAsync(cancellationToken);
        var lastByChat = lastMessages.ToDictionary(message => message.ChatId);

        return rows.Select(row => new ChatView(
            row.Chat,
            row.Member,
            BuildPeer(row.Chat, peerByChat, profiles, users, contacts, privacy, reverseContacts),
            lastByChat.TryGetValue(row.Chat.Id, out var message) ? BuildLastMessage(message) : null)).ToArray();
    }

    private ChatPeerView? BuildPeer(
        Chat chat,
        IReadOnlyDictionary<Guid, ChatMember> peers,
        IReadOnlyDictionary<Guid, Domain.Users.UserProfile> profiles,
        IReadOnlyDictionary<Guid, Domain.Identity.User> users,
        IReadOnlyDictionary<Guid, Domain.Contacts.Contact> contacts,
        IReadOnlyDictionary<Guid, UserPrivacySettings> privacy,
        IReadOnlySet<Guid> reverseContacts)
    {
        if (chat.Type != ChatType.Direct || !peers.TryGetValue(chat.Id, out var peer))
        {
            return null;
        }

        profiles.TryGetValue(peer.UserId, out var profile);
        contacts.TryGetValue(peer.UserId, out var contact);
        privacy.TryGetValue(peer.UserId, out var settings);
        var isReverseContact = reverseContacts.Contains(peer.UserId);
        var phoneVisible = IsVisible(settings?.PhoneVisibility ?? PrivacyVisibility.Contacts, isReverseContact);
        var avatarVisible = IsVisible(settings?.AvatarVisibility ?? PrivacyVisibility.Everybody, isReverseContact);
        var phone = phoneVisible && users.TryGetValue(peer.UserId, out var user) ? TryDecryptPhone(user) : null;
        var displayName = ChatDisplayName.Resolve(
            contact?.LocalFirstName,
            contact?.LocalLastName,
            profile?.FirstName ?? string.Empty,
            profile?.LastName ?? string.Empty,
            profile?.Username,
            phone);
        return new ChatPeerView(peer.UserId, displayName, avatarVisible ? profile?.AvatarObjectId : null);
    }

    private ChatLastMessageView BuildLastMessage(Message message)
    {
        string? body = null;
        if (message.DeletedAt is null)
        {
            try
            {
                body = cipher.Decrypt(new EncryptedValue(
                    message.BodyCiphertext, message.BodyNonce, message.BodyTag, message.BodyKeyVersion));
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or System.Security.Cryptography.CryptographicException)
            {
                logger.LogWarning(exception, "Could not decrypt last message {MessageId} in chat {ChatId}.", message.Id, message.ChatId);
                body = "Сообщение";
            }
        }

        return new ChatLastMessageView(
            message.Id, message.SenderUserId, message.Kind, body, message.CreatedAt, message.DeletedAt);
    }

    private string? TryDecryptPhone(Domain.Identity.User user)
    {
        try
        {
            return cipher.Decrypt(new EncryptedValue(
                user.PhoneCiphertext, user.PhoneNonce, user.PhoneTag, user.PhoneKeyVersion));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            logger.LogWarning(exception, "Could not decrypt phone for user {UserId} while building chat summary.", user.Id);
            return null;
        }
    }

    private static bool IsVisible(PrivacyVisibility visibility, bool isContact) =>
        visibility == PrivacyVisibility.Everybody || visibility == PrivacyVisibility.Contacts && isContact;

    private sealed record SummaryRow(Chat Chat, ChatMember Member);
}
