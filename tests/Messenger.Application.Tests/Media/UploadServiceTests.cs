using System.Security.Cryptography;
using Messenger.Application.Media;
using Messenger.Domain.Common;
using Messenger.Domain.Media;

namespace Messenger.Application.Tests.Media;

public sealed class UploadServiceTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    [Fact]
    public async Task Completed_private_object_gets_short_lived_authorization_only_for_owner()
    {
        var fixture = Fixture(Png, "image/png", "photo.png");
        var session = await fixture.Service.CreateAsync(fixture.Owner, fixture.Command);

        var stored = await fixture.Service.CompleteAsync(fixture.Owner, session.Id);
        var download = await fixture.Service.AuthorizeDownloadAsync(fixture.Owner, stored.Id);

        Assert.Equal(StoredObjectStatus.Available, stored.Status);
        Assert.Equal(fixture.Clock.UtcNow.AddMinutes(5), download.ExpiresAt);
        await Assert.ThrowsAsync<MediaException>(() =>
            fixture.Service.AuthorizeDownloadAsync(Guid.NewGuid(), stored.Id));
    }

    [Fact]
    public async Task Expired_upload_is_deleted_and_never_becomes_usable()
    {
        var fixture = Fixture(Png, "image/png", "photo.png");
        var session = await fixture.Service.CreateAsync(fixture.Owner, fixture.Command);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddHours(2);

        await Assert.ThrowsAsync<MediaException>(() => fixture.Service.CompleteAsync(fixture.Owner, session.Id));

        Assert.Contains(session.ObjectKey, fixture.Objects.Deleted);
    }

    [Theory]
    [InlineData("photo.png", "image/jpeg", false, false)]
    [InlineData("photo.jpg", "image/jpeg", false, false)]
    [InlineData("photo.png", "image/png", true, false)]
    [InlineData("photo.png", "image/png", false, true)]
    [InlineData("photo.png", "image/png", false, false)]
    public async Task Spoofed_oversized_wrong_checksum_or_unscanned_upload_is_rejected(
        string fileName, string mimeType, bool oversized, bool scannerRejects)
    {
        var bytes = oversized ? new byte[FileSignatureInspector.MaxImageBytes + 1] : Png;
        var fixture = Fixture(bytes, mimeType, fileName, scannerRejects);
        if (!oversized && fileName == "photo.png" && mimeType == "image/png" && !scannerRejects)
        {
            fixture.Command = fixture.Command with { Sha256 = new string('0', 64) };
        }

        var session = await fixture.Service.CreateAsync(fixture.Owner, fixture.Command);
        var error = await Assert.ThrowsAsync<MediaException>(() => fixture.Service.CompleteAsync(fixture.Owner, session.Id));

        Assert.Contains(error.Code, new[]
        {
            MediaError.FileTypeMismatch, MediaError.FileTooLarge, MediaError.ChecksumMismatch, MediaError.ScanRejected
        });
        Assert.DoesNotContain(fixture.Store.Objects, value => value.Status == StoredObjectStatus.Available);
    }

    [Fact]
    public async Task Cleanup_removes_expired_orphan_from_object_store()
    {
        var fixture = Fixture(Png, "image/png", "photo.png");
        var session = await fixture.Service.CreateAsync(fixture.Owner, fixture.Command);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddHours(2);

        await fixture.Service.CleanupExpiredAsync();

        Assert.Contains(session.ObjectKey, fixture.Objects.Deleted);
        Assert.Equal(UploadStatus.Expired, fixture.Store.Sessions.Single().Status);
    }

    private static TestFixture Fixture(byte[] bytes, string mime, string fileName, bool scanRejects = false)
    {
        var owner = Guid.NewGuid();
        var clock = new MutableClock { UtcNow = new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero) };
        var objects = new MemoryObjectStore(bytes, mime);
        var store = new MemoryUploadStore();
        var command = new CreateUploadCommand(fileName, mime, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var service = new UploadService(store, objects, new FileSignatureInspector(), new Scanner(scanRejects), clock);
        return new TestFixture(owner, clock, objects, store, service, command);
    }

    private sealed record TestFixture(
        Guid Owner, MutableClock Clock, MemoryObjectStore Objects, MemoryUploadStore Store,
        UploadService Service, CreateUploadCommand InitialCommand)
    {
        public CreateUploadCommand Command { get; set; } = InitialCommand;
    }

    private sealed class MutableClock : IClock { public DateTimeOffset UtcNow { get; set; } }

    private sealed class Scanner(bool reject) : IMalwareScanner
    {
        public Task<bool> IsSafeAsync(Stream content, CancellationToken ct) => Task.FromResult(!reject);
    }

    private sealed class MemoryObjectStore(byte[] bytes, string contentType) : IObjectStore
    {
        public List<string> Deleted { get; } = [];
        public Task<string> CreateUploadUrlAsync(string key, string mime, TimeSpan lifetime, CancellationToken ct) => Task.FromResult($"https://upload/{key}");
        public Task<ObjectMetadata> GetMetadataAsync(string key, CancellationToken ct) => Task.FromResult(new ObjectMetadata(bytes.LongLength, contentType));
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        public Task<string> CreateDownloadUrlAsync(string key, TimeSpan lifetime, CancellationToken ct) => Task.FromResult($"https://download/{key}");
        public Task DeleteAsync(string key, CancellationToken ct) { Deleted.Add(key); return Task.CompletedTask; }
    }

    private sealed class MemoryUploadStore : IUploadStore
    {
        public List<UploadSession> Sessions { get; } = [];
        public List<StoredObject> Objects { get; } = [];
        public Task AddSessionAsync(UploadSession session, CancellationToken ct) { Sessions.Add(session); return Task.CompletedTask; }
        public Task<UploadSession?> FindSessionAsync(Guid id, CancellationToken ct) => Task.FromResult(Sessions.SingleOrDefault(x => x.Id == id));
        public Task AddObjectAsync(StoredObject value, CancellationToken ct) { Objects.Add(value); return Task.CompletedTask; }
        public Task<StoredObject?> FindObjectAsync(Guid id, CancellationToken ct) => Task.FromResult(Objects.SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<UploadSession>> ListExpiredPendingAsync(DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UploadSession>>(Sessions.Where(x => x.Status == UploadStatus.Pending && x.ExpiresAt <= now).ToArray());
        public Task<bool> CanAccessObjectAsync(Guid userId, Guid objectId, CancellationToken ct) => Task.FromResult(false);
        public Task AttachToMessageAsync(Guid actorId, Guid messageId, IReadOnlyList<Guid> objectIds, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<StoredObject>> ListChatMediaAsync(Guid actorId, Guid chatId, CancellationToken ct) => Task.FromResult<IReadOnlyList<StoredObject>>([]);
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
