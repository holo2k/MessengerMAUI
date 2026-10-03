using Messenger.Domain.Common;

namespace Messenger.Domain.Chats;

public sealed class ChatFolder : Entity
{
    private ChatFolder()
    {
    }

    public ChatFolder(Guid id, Guid userId, string title, int position, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Folder title is required.", nameof(title));
        }
        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
        Id = id;
        UserId = userId;
        Title = title.Trim();
        Position = position;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public int Position { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Folder title is required.", nameof(title));
        }
        Title = title.Trim();
    }

    public void ChangePosition(int position)
    {
        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
        Position = position;
    }
}

public sealed class ChatFolderItem
{
    private ChatFolderItem()
    {
    }

    public ChatFolderItem(Guid folderId, Guid chatId)
    {
        FolderId = folderId;
        ChatId = chatId;
    }

    public Guid FolderId { get; private set; }
    public Guid ChatId { get; private set; }
}
