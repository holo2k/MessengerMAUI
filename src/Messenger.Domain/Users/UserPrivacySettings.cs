using Messenger.Domain.Common;

namespace Messenger.Domain.Users;

public enum PrivacyVisibility
{
    Everybody = 1,
    Contacts = 2,
    Nobody = 3
}

public sealed class UserPrivacySettings : Entity
{
    private UserPrivacySettings()
    {
    }

    public UserPrivacySettings(Guid userId)
    {
        Id = userId;
        UserId = userId;
    }

    public Guid UserId { get; private set; }
    public PrivacyVisibility PhoneVisibility { get; private set; } = PrivacyVisibility.Contacts;
    public PrivacyVisibility AvatarVisibility { get; private set; } = PrivacyVisibility.Everybody;
    public PrivacyVisibility LastSeenVisibility { get; private set; } = PrivacyVisibility.Contacts;

    public void Update(
        PrivacyVisibility phone,
        PrivacyVisibility avatar,
        PrivacyVisibility lastSeen)
    {
        PhoneVisibility = phone;
        AvatarVisibility = avatar;
        LastSeenVisibility = lastSeen;
    }
}
