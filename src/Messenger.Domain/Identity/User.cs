using Messenger.Domain.Common;

namespace Messenger.Domain.Identity;

public sealed class User : Entity
{
    private User()
    {
    }

    public User(
        Guid id,
        string phoneCiphertext,
        string phoneNonce,
        string phoneTag,
        int phoneKeyVersion,
        string phoneBlindIndex,
        DateTimeOffset createdAt)
    {
        Id = id;
        PhoneCiphertext = phoneCiphertext;
        PhoneNonce = phoneNonce;
        PhoneTag = phoneTag;
        PhoneKeyVersion = phoneKeyVersion;
        PhoneBlindIndex = phoneBlindIndex;
        CreatedAt = createdAt;
    }

    public string PhoneCiphertext { get; private set; } = string.Empty;
    public string PhoneNonce { get; private set; } = string.Empty;
    public string PhoneTag { get; private set; } = string.Empty;
    public int PhoneKeyVersion { get; private set; }
    public string PhoneBlindIndex { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public void ChangePhone(
        string ciphertext,
        string nonce,
        string tag,
        int keyVersion,
        string blindIndex)
    {
        PhoneCiphertext = ciphertext;
        PhoneNonce = nonce;
        PhoneTag = tag;
        PhoneKeyVersion = keyVersion;
        PhoneBlindIndex = blindIndex;
    }

    public void Deactivate(DateTimeOffset now)
    {
        DeactivatedAt ??= now;
        PhoneBlindIndex = $"deleted:{Id:N}";
    }
}
