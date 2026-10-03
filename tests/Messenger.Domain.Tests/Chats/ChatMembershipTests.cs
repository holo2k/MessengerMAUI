using Messenger.Domain.Chats;
using Messenger.Domain.Contacts;

namespace Messenger.Domain.Tests.Chats;

public sealed class ChatMembershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Contact_cannot_target_its_owner()
    {
        var userId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            new Contact(Guid.NewGuid(), userId, userId, null, null, false, Now));
    }

    [Fact]
    public void Local_contact_alias_does_not_change_the_target_identity()
    {
        var targetId = Guid.NewGuid();
        var contact = new Contact(Guid.NewGuid(), Guid.NewGuid(), targetId, "Домашний", "Врач", false, Now);

        contact.Rename("Рабочий", "Врач");

        Assert.Equal(targetId, contact.TargetUserId);
        Assert.Equal("Рабочий", contact.LocalFirstName);
    }

    [Fact]
    public void Direct_pair_is_canonical_regardless_of_creation_order()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var forward = DirectChatPair.Create(Guid.NewGuid(), first, second);
        var reverse = DirectChatPair.Create(Guid.NewGuid(), second, first);

        Assert.Equal(forward.LowerUserId, reverse.LowerUserId);
        Assert.Equal(forward.HigherUserId, reverse.HigherUserId);
    }

    [Fact]
    public void Member_cannot_promote_another_member()
    {
        var chatId = Guid.NewGuid();
        var actor = Member(chatId, ChatMemberRole.Member);
        var target = Member(chatId, ChatMemberRole.Member);

        var error = Assert.Throws<ChatRuleException>(() =>
            target.ChangeRole(actor, ChatMemberRole.Admin, [actor, target]));

        Assert.Equal(ChatRuleError.Forbidden, error.Code);
    }

    [Fact]
    public void Last_active_owner_cannot_leave_or_be_demoted()
    {
        var chatId = Guid.NewGuid();
        var owner = Member(chatId, ChatMemberRole.Owner);
        var member = Member(chatId, ChatMemberRole.Member);

        Assert.Equal(ChatRuleError.LastOwner,
            Assert.Throws<ChatRuleException>(() => owner.Leave([owner, member], Now)).Code);
        Assert.Equal(ChatRuleError.LastOwner,
            Assert.Throws<ChatRuleException>(() => owner.ChangeRole(owner, ChatMemberRole.Admin, [owner, member])).Code);
    }

    [Fact]
    public void Admin_can_remove_a_member_but_not_an_owner()
    {
        var chatId = Guid.NewGuid();
        var owner = Member(chatId, ChatMemberRole.Owner);
        var admin = Member(chatId, ChatMemberRole.Admin);
        var member = Member(chatId, ChatMemberRole.Member);

        member.Remove(admin, [owner, admin, member], Now);

        Assert.Equal(Now, member.LeftAt);
        Assert.Equal(ChatRuleError.Forbidden,
            Assert.Throws<ChatRuleException>(() => owner.Remove(admin, [owner, admin], Now)).Code);
    }

    [Fact]
    public void Hidden_chat_can_be_reactivated_and_folder_position_can_change()
    {
        var member = Member(Guid.NewGuid(), ChatMemberRole.Member);
        member.Hide(Now);
        var folder = new ChatFolder(Guid.NewGuid(), member.UserId, "Работа", 4, Now);

        member.Reactivate();
        folder.ChangePosition(1);

        Assert.Null(member.HiddenAt);
        Assert.Equal(1, folder.Position);
    }

    private static ChatMember Member(Guid chatId, ChatMemberRole role) =>
        new(chatId, Guid.NewGuid(), role, Now);
}
