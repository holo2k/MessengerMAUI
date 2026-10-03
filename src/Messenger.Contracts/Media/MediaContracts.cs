namespace Messenger.Contracts.Media;

public sealed record CreateUploadRequest(string FileName, string ContentType, long Size, string Sha256);
public sealed record UploadSessionResponse(Guid Id, string ObjectKey, string UploadUrl, DateTimeOffset ExpiresAt);
public sealed record StoredObjectResponse(Guid Id, string FileName, string ContentType, long Size, string Sha256, DateTimeOffset CreatedAt);
public sealed record DownloadAuthorizationResponse(string Url, DateTimeOffset ExpiresAt);
