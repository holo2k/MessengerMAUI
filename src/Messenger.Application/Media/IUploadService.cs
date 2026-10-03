using Messenger.Domain.Media;

namespace Messenger.Application.Media;

public enum MediaError
{
    NotFound, Forbidden, Expired, AlreadyCompleted, InvalidDeclaration,
    FileTooLarge, FileTypeMismatch, ChecksumMismatch, ScanRejected, NotAvailable
}

public sealed class MediaException(MediaError code) : Exception(code.ToString())
{
    public MediaError Code { get; } = code;
}

public sealed record CreateUploadCommand(string FileName, string ContentType, long Size, string Sha256);
public sealed record UploadSessionResult(Guid Id, string ObjectKey, string UploadUrl, DateTimeOffset ExpiresAt);
public sealed record DownloadAuthorization(string Url, DateTimeOffset ExpiresAt);
public sealed record ObjectMetadata(long Size, string? ContentType);

public interface IObjectStore
{
    Task<string> CreateUploadUrlAsync(string key, string contentType, TimeSpan lifetime, CancellationToken ct);
    Task<ObjectMetadata> GetMetadataAsync(string key, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task<string> CreateDownloadUrlAsync(string key, TimeSpan lifetime, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

public interface IFileInspector
{
    void Validate(string fileName, string declaredContentType, string storedContentType, long size, ReadOnlySpan<byte> header);
}

public interface IMalwareScanner
{
    Task<bool> IsSafeAsync(Stream content, CancellationToken ct);
}

public interface IUploadStore
{
    Task AddSessionAsync(UploadSession session, CancellationToken ct);
    Task<UploadSession?> FindSessionAsync(Guid id, CancellationToken ct);
    Task AddObjectAsync(StoredObject value, CancellationToken ct);
    Task<StoredObject?> FindObjectAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<UploadSession>> ListExpiredPendingAsync(DateTimeOffset now, CancellationToken ct);
    Task<bool> CanAccessObjectAsync(Guid userId, Guid objectId, CancellationToken ct);
    Task AttachToMessageAsync(Guid actorId, Guid messageId, IReadOnlyList<Guid> objectIds, CancellationToken ct);
    Task<IReadOnlyList<StoredObject>> ListChatMediaAsync(Guid actorId, Guid chatId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IUploadService
{
    Task<UploadSessionResult> CreateAsync(Guid actorId, CreateUploadCommand command, CancellationToken ct = default);
    Task<StoredObject> CompleteAsync(Guid actorId, Guid uploadId, CancellationToken ct = default);
    Task<DownloadAuthorization> AuthorizeDownloadAsync(Guid actorId, Guid objectId, CancellationToken ct = default);
    Task AttachToMessageAsync(Guid actorId, Guid messageId, IReadOnlyList<Guid> objectIds, CancellationToken ct = default);
    Task<IReadOnlyList<StoredObject>> ListChatMediaAsync(Guid actorId, Guid chatId, CancellationToken ct = default);
    Task ValidateOwnedAvailableAsync(Guid actorId, Guid objectId, CancellationToken ct = default);
    Task CleanupExpiredAsync(CancellationToken ct = default);
}
