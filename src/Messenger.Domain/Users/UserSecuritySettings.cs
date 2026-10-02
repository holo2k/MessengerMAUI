using Messenger.Domain.Common;

namespace Messenger.Domain.Users;

public sealed class UserSecuritySettings : Entity
{
    private UserSecuritySettings()
    {
    }

    public UserSecuritySettings(Guid userId)
    {
        Id = userId;
        UserId = userId;
    }

    public Guid UserId { get; private set; }
    public bool TwoFactorEnabled { get; private set; }
    public string? EmailCiphertext { get; private set; }
    public string? EmailNonce { get; private set; }
    public string? EmailTag { get; private set; }
    public int? EmailKeyVersion { get; private set; }

    public static UserSecuritySettings Enabled(
        Guid userId,
        string ciphertext,
        string nonce,
        string tag,
        int keyVersion)
    {
        var settings = new UserSecuritySettings(userId);
        settings.Enable(ciphertext, nonce, tag, keyVersion);
        return settings;
    }

    public void Enable(string ciphertext, string nonce, string tag, int keyVersion)
    {
        EmailCiphertext = ciphertext;
        EmailNonce = nonce;
        EmailTag = tag;
        EmailKeyVersion = keyVersion;
        TwoFactorEnabled = true;
    }

    public void Disable()
    {
        TwoFactorEnabled = false;
        EmailCiphertext = null;
        EmailNonce = null;
        EmailTag = null;
        EmailKeyVersion = null;
    }
}
