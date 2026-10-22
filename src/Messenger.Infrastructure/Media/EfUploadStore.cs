using Messenger.Application.Media;
using Messenger.Domain.Media;
using Messenger.Domain.Users;
using Messenger.Domain.Music;
using Messenger.Domain.Chats;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Media;

public sealed class EfUploadStore(MessengerDbContext db) : IUploadStore
{
    public Task AddSessionAsync(UploadSession session, CancellationToken ct) => db.UploadSessions.AddAsync(session, ct).AsTask();
    public Task<UploadSession?> FindSessionAsync(Guid id, CancellationToken ct) => db.UploadSessions.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task AddObjectAsync(StoredObject value, CancellationToken ct) => db.StoredObjects.AddAsync(value, ct).AsTask();
    public Task<StoredObject?> FindObjectAsync(Guid id, CancellationToken ct) => db.StoredObjects.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<UploadSession>> ListExpiredPendingAsync(DateTimeOffset now, CancellationToken ct) =>
        await db.UploadSessions.Where(x => x.Status == UploadStatus.Pending && x.ExpiresAt <= now).ToArrayAsync(ct);

    public async Task<bool> CanAccessObjectAsync(Guid userId, Guid objectId, CancellationToken ct)
    {
        if (await db.MusicTracks.AnyAsync(track => track.CoverObjectId == objectId &&
                track.Status == MusicTrackStatus.Available, ct)) return true;

        if (await (from attachment in db.MessageAttachments
            join message in db.Messages on attachment.MessageId equals message.Id
            join member in db.ChatMembers on message.ChatId equals member.ChatId
            join chat in db.Chats on message.ChatId equals chat.Id
            where attachment.ObjectId == objectId && member.UserId == userId && member.LeftAt == null
                  && message.DeletedAt == null && chat.DeletedAt == null
                  && !db.HiddenMessages.Any(hidden => hidden.MessageId == message.Id && hidden.UserId == userId)
            select attachment).AnyAsync(ct))
        {
            return true;
        }

        var avatarObjectId = objectId.ToString();
        if (await (from profile in db.UserProfiles
            join privacyValue in db.UserPrivacySettings on profile.UserId equals privacyValue.UserId into privacyRows
            from privacy in privacyRows.DefaultIfEmpty()
            where profile.AvatarObjectId == avatarObjectId &&
                  (profile.UserId == userId || privacy == null ||
                   privacy.AvatarVisibility == PrivacyVisibility.Everybody ||
                   privacy.AvatarVisibility == PrivacyVisibility.Contacts && db.Contacts.Any(contact =>
                       contact.OwnerUserId == profile.UserId && contact.TargetUserId == userId))
            select profile).AnyAsync(ct))
        {
            return true;
        }

        return await (from chat in db.Chats
            join member in db.ChatMembers on chat.Id equals member.ChatId
            where chat.AvatarObjectId == avatarObjectId && member.UserId == userId && member.LeftAt == null
            select chat).AnyAsync(ct);
    }

    public async Task AttachToMessageAsync(Guid actorId, Guid messageId, IReadOnlyList<Guid> objectIds, CancellationToken ct)
    {
        var message = await db.Messages.SingleOrDefaultAsync(x => x.Id == messageId && x.SenderUserId == actorId, ct)
            ?? throw new MediaException(MediaError.NotFound);
        var existing = await db.MessageAttachments.Where(x => x.MessageId == messageId)
            .OrderBy(x => x.Position).Select(x => x.ObjectId).ToArrayAsync(ct);
        if (existing.Length > 0)
        {
            if (existing.SequenceEqual(objectIds)) return;
            throw new MediaException(MediaError.InvalidDeclaration);
        }
        var objects = await db.StoredObjects.Where(x => objectIds.Contains(x.Id)).ToArrayAsync(ct);
        if (objects.Length != objectIds.Count || objects.Any(x => x.OwnerUserId != actorId || x.Status != StoredObjectStatus.Available))
        {
            throw new MediaException(MediaError.NotFound);
        }
        if (message.Kind is MessageKind.Voice or MessageKind.VideoNote &&
            (objects.Length != 1 || !objects[0].ContentType.StartsWith(message.Kind == MessageKind.Voice ? "audio/" : "video/", StringComparison.OrdinalIgnoreCase)))
            throw new MediaException(MediaError.InvalidDeclaration);
        for (var index = 0; index < objectIds.Count; index++)
        {
            db.MessageAttachments.Add(new MessageAttachment(message.Id, objectIds[index], index));
        }
    }

    public async Task<IReadOnlyList<StoredObject>> ListChatMediaAsync(Guid actorId, Guid chatId, CancellationToken ct)
    {
        var active = await db.ChatMembers.AnyAsync(x => x.ChatId == chatId && x.UserId == actorId && x.LeftAt == null && x.HiddenAt == null, ct);
        if (!active)
        {
            throw new MediaException(MediaError.NotFound);
        }
        return await (from attachment in db.MessageAttachments
                      join message in db.Messages on attachment.MessageId equals message.Id
                      join value in db.StoredObjects on attachment.ObjectId equals value.Id
                      where message.ChatId == chatId && value.Status == StoredObjectStatus.Available && message.DeletedAt == null
                            && !db.HiddenMessages.Any(hidden => hidden.MessageId == message.Id && hidden.UserId == actorId)
                      orderby message.Sequence descending, attachment.Position
                      select value).ToArrayAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
