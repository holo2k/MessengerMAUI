using System.Security.Cryptography;
using Messenger.Domain.Common;
using Messenger.Domain.Media;

namespace Messenger.Application.Media;

public sealed class UploadService(
    IUploadStore store,
    IObjectStore objects,
    IFileInspector inspector,
    IMalwareScanner scanner,
    IClock clock) : IUploadService
{
    private static readonly TimeSpan UploadLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan DownloadLifetime = TimeSpan.FromMinutes(5);

    public async Task<UploadSessionResult> CreateAsync(Guid actorId, CreateUploadCommand command, CancellationToken ct = default)
    {
        if (actorId == Guid.Empty || string.IsNullOrWhiteSpace(command.FileName) ||
            string.IsNullOrWhiteSpace(command.ContentType) || command.Size <= 0 ||
            command.Sha256.Length != 64 || !command.Sha256.All(Uri.IsHexDigit))
        {
            throw new MediaException(MediaError.InvalidDeclaration);
        }
        var id = Guid.NewGuid();
        var key = $"private/{actorId:N}/{id:N}";
        var expiresAt = clock.UtcNow.Add(UploadLifetime);
        var session = new UploadSession(id, actorId, key, Path.GetFileName(command.FileName),
            command.ContentType.ToLowerInvariant(), command.Size, command.Sha256.ToLowerInvariant(), clock.UtcNow, expiresAt);
        await store.AddSessionAsync(session, ct);
        await store.SaveChangesAsync(ct);
        var url = await objects.CreateUploadUrlAsync(key, session.ContentType, UploadLifetime, ct);
        return new UploadSessionResult(id, key, url, expiresAt);
    }

    public async Task<StoredObject> CompleteAsync(Guid actorId, Guid uploadId, CancellationToken ct = default)
    {
        var session = await store.FindSessionAsync(uploadId, ct);
        if (session is null || session.OwnerUserId != actorId)
        {
            throw new MediaException(MediaError.NotFound);
        }
        if (session.Status != UploadStatus.Pending)
        {
            throw new MediaException(MediaError.AlreadyCompleted);
        }
        if (session.ExpiresAt <= clock.UtcNow)
        {
            session.Expire();
            await objects.DeleteAsync(session.ObjectKey, ct);
            await store.SaveChangesAsync(ct);
            throw new MediaException(MediaError.Expired);
        }

        try
        {
            var metadata = await objects.GetMetadataAsync(session.ObjectKey, ct);
            await using var remote = await objects.OpenReadAsync(session.ObjectKey, ct);
            await using var content = new MemoryStream();
            await remote.CopyToAsync(content, ct);
            var bytes = content.ToArray();
            inspector.Validate(session.FileName, session.ContentType, metadata.ContentType ?? string.Empty,
                metadata.Size, bytes.AsSpan(0, Math.Min(bytes.Length, 16)));
            if (metadata.Size != session.DeclaredSize || bytes.LongLength != metadata.Size)
            {
                throw new MediaException(MediaError.FileTooLarge);
            }
            var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(checksum), Convert.FromHexString(session.Sha256)))
            {
                throw new MediaException(MediaError.ChecksumMismatch);
            }
            content.Position = 0;
            if (!await scanner.IsSafeAsync(content, ct))
            {
                throw new MediaException(MediaError.ScanRejected);
            }

            var stored = new StoredObject(Guid.NewGuid(), actorId, session.ObjectKey, session.FileName,
                session.ContentType, metadata.Size, checksum, StoredObjectStatus.Available, clock.UtcNow);
            await store.AddObjectAsync(stored, ct);
            session.Complete(stored.Id);
            await store.SaveChangesAsync(ct);
            return stored;
        }
        catch (MediaException)
        {
            session.Reject();
            await objects.DeleteAsync(session.ObjectKey, ct);
            await store.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<DownloadAuthorization> AuthorizeDownloadAsync(
        Guid actorId, Guid objectId, CancellationToken ct = default)
    {
        var stored = await store.FindObjectAsync(objectId, ct);
        if (stored is null || stored.Status != StoredObjectStatus.Available)
        {
            throw new MediaException(MediaError.NotFound);
        }
        if (stored.OwnerUserId != actorId && !await store.CanAccessObjectAsync(actorId, objectId, ct))
        {
            throw new MediaException(MediaError.NotFound);
        }
        var expiresAt = clock.UtcNow.Add(DownloadLifetime);
        return new DownloadAuthorization(
            await objects.CreateDownloadUrlAsync(stored.ObjectKey, DownloadLifetime, ct), expiresAt);
    }

    public async Task CleanupExpiredAsync(CancellationToken ct = default)
    {
        foreach (var session in await store.ListExpiredPendingAsync(clock.UtcNow, ct))
        {
            session.Expire();
            await objects.DeleteAsync(session.ObjectKey, ct);
        }
        await store.SaveChangesAsync(ct);
    }

    public async Task AttachToMessageAsync(
        Guid actorId, Guid messageId, IReadOnlyList<Guid> objectIds, CancellationToken ct = default)
    {
        if (objectIds.Count > 10 || objectIds.Distinct().Count() != objectIds.Count)
        {
            throw new MediaException(MediaError.InvalidDeclaration);
        }
        await store.AttachToMessageAsync(actorId, messageId, objectIds, ct);
        await store.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<StoredObject>> ListChatMediaAsync(
        Guid actorId, Guid chatId, CancellationToken ct = default) =>
        store.ListChatMediaAsync(actorId, chatId, ct);

    public async Task ValidateOwnedAvailableAsync(Guid actorId, Guid objectId, CancellationToken ct = default)
    {
        var value = await store.FindObjectAsync(objectId, ct);
        if (value is null || value.OwnerUserId != actorId || value.Status != StoredObjectStatus.Available)
        {
            throw new MediaException(MediaError.NotFound);
        }
    }
}
