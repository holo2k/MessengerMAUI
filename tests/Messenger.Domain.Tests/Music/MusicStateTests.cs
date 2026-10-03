using Messenger.Domain.Music;

namespace Messenger.Domain.Tests.Music;

public sealed class MusicStateTests
{
    [Fact]
    public void Track_requires_declaration_before_approval_and_can_be_blocked_or_deleted()
    {
        var track = Track();
        Assert.Throws<MusicRuleException>(() => track.Approve(DateTimeOffset.UtcNow));

        track.DeclareRights(track.UploaderUserId, "Я обладаю правами", DateTimeOffset.UtcNow);
        track.Approve(DateTimeOffset.UtcNow);
        track.Block(DateTimeOffset.UtcNow);

        Assert.Equal(MusicTrackStatus.Blocked, track.Status);
        track.Delete(DateTimeOffset.UtcNow);
        Assert.Equal(MusicTrackStatus.Deleted, track.Status);
    }

    [Fact]
    public void Rights_declaration_is_single_and_moderation_action_is_immutable()
    {
        var track = Track();
        track.DeclareRights(track.UploaderUserId, "Подтверждаю права", DateTimeOffset.UtcNow);

        Assert.Throws<MusicRuleException>(() => track.DeclareRights(track.UploaderUserId, "Ещё раз", DateTimeOffset.UtcNow));
        var action = new ModerationAction(Guid.NewGuid(), track.Id, Guid.NewGuid(), "approved", "Проверено", DateTimeOffset.UtcNow);
        Assert.Equal("approved", action.Action);
    }

    private static MusicTrack Track() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Название", "Исполнитель", 120_000, null, DateTimeOffset.UtcNow);
}
