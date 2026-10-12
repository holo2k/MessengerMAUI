using Messenger.Domain.Chats;

namespace Messenger.Application.Chats;

public sealed record ChatPeerView(Guid UserId, string DisplayName, string? AvatarObjectId);

public sealed record ChatLastMessageView(
    Guid Id,
    Guid SenderUserId,
    MessageKind Kind,
    string? Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record ChatView(
    Chat Chat,
    ChatMember Member,
    ChatPeerView? Peer = null,
    ChatLastMessageView? LastMessage = null);

public sealed record ChatSummaryPage(IReadOnlyList<ChatView> Items, string? NextCursor);

public interface IChatSummaryReader
{
    Task<ChatView?> GetAsync(Guid requesterUserId, Guid chatId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatView>> ListAsync(
        Guid requesterUserId,
        DateTimeOffset? afterUpdatedAt,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken);
}

public static class ChatDisplayName
{
    public static string Resolve(
        string? localFirstName,
        string? localLastName,
        string firstName,
        string lastName,
        string? username,
        string? visiblePhone)
    {
        var localName = Join(localFirstName, localLastName);
        if (localName.Length > 0)
        {
            return localName;
        }

        var profileName = Join(firstName, lastName);
        if (profileName.Length > 0)
        {
            return profileName;
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            return username.Trim();
        }

        return string.IsNullOrWhiteSpace(visiblePhone) ? "Пользователь" : visiblePhone.Trim();
    }

    private static string Join(string? first, string? last) =>
        string.Join(' ', new[] { first?.Trim(), last?.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
