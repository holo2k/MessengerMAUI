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
        await fixture.Service.DeclareRightsAsync(fixture.User, track.Id, "Права мои");
        await fixture.Moderation.DecideAsync(fixture.Admin, track.Id, "approve", "ok");

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
        await fixture.Service.DeclareRightsAsync(fixture.User, track.Id, "Права мои");
        await fixture.Moderation.DecideAsync(fixture.Admin, track.Id, "approve", "ok");
        await fixture.Service.AuthorizeStreamAsync(fixture.User, track.Id);

        await fixture.Moderation.DecideAsync(fixture.Admin, track.Id, "block", "Жалоба правообладателя");

        await Assert.ThrowsAsync<MusicRuleException>(() => fixture.Service.AuthorizeStreamAsync(fixture.User, track.Id));
        Assert.Equal(1, fixture.Uploads.AuthorizationCalls);
        Assert.Equal(2, fixture.Store.Actions.Count);
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

    private sealed class MemoryMusicStore : IMusicStore
    {
        public List<MusicTrack> Tracks { get; } = [];
        public List<UserMusicTrack> Library { get; } = [];
        public List<CopyrightClaim> Claims { get; } = [];
        public List<ModerationAction> Actions { get; } = [];
        public Task AddTrackAsync(MusicTrack track, CancellationToken ct) { Tracks.Add(track); return Task.CompletedTask; }
        public Task AddDeclarationAsync(RightsDeclaration declaration, CancellationToken ct) => Task.CompletedTask;
        public Task<MusicTrack?> FindTrackAsync(Guid id, CancellationToken ct) => Task.FromResult(Tracks.SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<MusicTrack>> SearchAsync(string query, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<MusicTrack>>(Tracks.Where(x => x.Status == MusicTrackStatus.Available && (x.Title + x.Artist).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray());
        public Task<IReadOnlyList<MusicTrack>> ListLibraryAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<MusicTrack>>((from item in Library join track in Tracks on item.TrackId equals track.Id where item.UserId == userId && track.Status == MusicTrackStatus.Available select track).ToArray());
        public Task<bool> LibraryContainsAsync(Guid userId, Guid trackId, CancellationToken ct) => Task.FromResult(Library.Any(x => x.UserId == userId && x.TrackId == trackId));
        public Task AddLibraryAsync(UserMusicTrack item, CancellationToken ct) { Library.Add(item); return Task.CompletedTask; }
        public Task RemoveLibraryAsync(Guid userId, Guid trackId, CancellationToken ct) { Library.RemoveAll(x => x.UserId == userId && x.TrackId == trackId); return Task.CompletedTask; }
        public Task AddClaimAsync(CopyrightClaim claim, CancellationToken ct) { Claims.Add(claim); return Task.CompletedTask; }
        public Task AddActionAsync(ModerationAction action, CancellationToken ct) { Actions.Add(action); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
