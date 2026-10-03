using Messenger.Domain.Common;

namespace Messenger.Domain.Media;

public enum UploadStatus { Pending, Completed, Rejected, Expired }
public enum StoredObjectStatus { NotScanned, Available, Rejected, Deleted }

public sealed class UploadSession : Entity
{
    private UploadSession() { }

    public UploadSession(Guid id, Guid ownerUserId, string objectKey, string fileName, string contentType,
        long declaredSize, string sha256, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        ObjectKey = objectKey;
        FileName = fileName;
        ContentType = contentType;
        DeclaredSize = declaredSize;
        Sha256 = sha256;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid OwnerUserId { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long DeclaredSize { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public UploadStatus Status { get; private set; }
    public Guid? StoredObjectId { get; private set; }

    public void Complete(Guid objectId) { Status = UploadStatus.Completed; StoredObjectId = objectId; }
    public void Reject() => Status = UploadStatus.Rejected;
    public void Expire() => Status = UploadStatus.Expired;
}

public sealed class StoredObject : Entity
{
    private StoredObject() { }

    public StoredObject(Guid id, Guid ownerUserId, string objectKey, string fileName, string contentType,
        long size, string sha256, StoredObjectStatus status, DateTimeOffset createdAt)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        ObjectKey = objectKey;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        Sha256 = sha256;
        Status = status;
        CreatedAt = createdAt;
    }

    public Guid OwnerUserId { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long Size { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public StoredObjectStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
