using Messenger.Application.Media;
using Messenger.Application.Music;
using Messenger.Domain.Common;
using Messenger.Domain.Media;
using Messenger.Domain.Music;

namespace Messenger.Application.Tests.Music;

public sealed class MusicServiceTests
{
    [Fact]
    public async Task Search_and_library_only_expose_available_tracks_and_add_is_idempotent()
    {
        var fixture = Fixture();
        var track = await fixture.Service.CreateAsync(fixture.User, Guid.NewGuid(), "Песня", "Автор", 1000, null);
        Assert.Equal(MusicTrackStatus.Available, track.Status);
        Assert.Null(track.Declaration);
        Assert.Single(await fixture.Service.ListLibraryAsync(fixture.User));
        await fixture.Service.AuthorizeStreamAsync(fixture.User, track.Id);

        await fixture.Service.AddToLibraryAsync(fixture.User, track.Id);
        await fixture.Service.AddToLibraryAsync(fixture.User, track.Id);

        Assert.Single(await fixture.Service.SearchAsync(fixture.User, "пес"));
        Assert.Single(await fixture.Service.ListLibraryAsync(fixture.User));
    }

    [Fact]
    public async Task Blocking_between_authorizations_prevents_new_stream_url()
    {
        var fixture = Fixture();
        var track = await fixture.Service.CreateAsync(fixture.User, Guid.NewGuid(), "Песня", "Автор", 1000, null);
        await fixture.Service.AuthorizeStreamAsync(fixture.User, track.Id);

        await fixture.Moderation.DecideAsync(fixture.Admin, track.Id, "block", "Жалоба правообладателя");

        await Assert.ThrowsAsync<MusicRuleException>(() => fixture.Service.AuthorizeStreamAsync(fixture.User, track.Id));
        Assert.Equal(1, fixture.Uploads.AuthorizationCalls);
        Assert.Single(fixture.Store.Actions);
    }

    [Fact]
    public async Task Non_admin_cannot_moderate_and_claim_is_recorded()
    {
        var fixture = Fixture();
        var track = await fixture.Service.CreateAsync(fixture.User, Guid.NewGuid(), "Песня", "Автор", 1000, null);
        await fixture.Service.SubmitClaimAsync(Guid.NewGuid(), track.Id, "Я правообладатель");

        await Assert.ThrowsAsync<MusicRuleException>(() => fixture.Moderation.DecideAsync(fixture.User, track.Id, "block", "нет"));
        Assert.Single(fixture.Store.Claims);
    }

    [Fact]
    public async Task Only_uploader_can_replace_the_cover_with_an_owned_image()
    {
        var fixture = Fixture();
        var firstCover = Guid.NewGuid();
        var nextCover = Guid.NewGuid();
        fixture.Uploads.Images.UnionWith([firstCover, nextCover]);
        var track = await fixture.Service.CreateAsync(fixture.User, Guid.NewGuid(), "Песня", "Автор", 1000, firstCover);
        Assert.Equal(firstCover, track.CoverObjectId);
        var updated = await fixture.Service.UpdateCoverAsync(fixture.User, track.Id, nextCover);
        Assert.Equal(nextCover, updated.CoverObjectId);
        Assert.Equal(MusicRuleError.Forbidden, (await Assert.ThrowsAsync<MusicRuleException>(() =>
            fixture.Service.UpdateCoverAsync(Guid.NewGuid(), track.Id, firstCover))).Code);
        await Assert.ThrowsAsync<MediaException>(() => fixture.Service.UpdateCoverAsync(fixture.User, track.Id, Guid.NewGuid()));
        Assert.Equal(nextCover, track.CoverObjectId);
    }

    private static TestFixture Fixture()
    {
        var user = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var store = new MemoryMusicStore();
        var uploads = new FakeUploads(user);
        var clock = new FixedClock();
        return new TestFixture(user, admin, store, uploads,
            new MusicService(store, uploads, clock), new MusicModerationService(store, clock, new MusicAdminOptions { UserIds = [admin] }));
    }

    private sealed record TestFixture(Guid User, Guid Admin, MemoryMusicStore Store, FakeUploads Uploads, MusicService Service, MusicModerationService Moderation);
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero); }

    private sealed class FakeUploads(Guid owner) : IUploadService
    {
        public HashSet<Guid> Images { get; } = [];
        public Task ValidateOwnedImageAsync(Guid actorId, Guid objectId, CancellationToken ct = default) =>
            actorId == owner && Images.Contains(objectId) ? Task.CompletedTask : Task.FromException(new MediaException(MediaError.FileTypeMismatch));
        public int AuthorizationCalls { get; private set; }
        public Task ValidateOwnedAvailableAsync(Guid actorId, Guid objectId, CancellationToken ct = default) =>
            actorId == owner ? Task.CompletedTask : Task.FromException(new MediaException(MediaError.NotFound));
        public Task<DownloadAuthorization> AuthorizeDownloadAsync(Guid actorId, Guid objectId, CancellationToken ct = default)
        { AuthorizationCalls++; return Task.FromResult(new DownloadAuthorization("https://stream", DateTimeOffset.UtcNow.AddMinutes(5))); }
        public Task<UploadSessionResult> CreateAsync(Guid actorId, CreateUploadCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StoredObject> CompleteAsync(Guid actorId, Guid uploadId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AttachToMessageAsync(Guid actorId, Guid messageId, IReadOnlyList<Guid> objectIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StoredObject>> ListChatMediaAsync(Guid actorId, Guid chatId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CleanupExpiredAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

}
