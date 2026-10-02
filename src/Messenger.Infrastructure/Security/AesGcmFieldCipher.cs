using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Messenger.Application.Security;

namespace Messenger.Infrastructure.Security;

public sealed class AesGcmFieldCipher : IFieldCipher
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IReadOnlyDictionary<int, byte[]> _keys;
    private readonly int _activeKeyVersion;

    public AesGcmFieldCipher(FieldCipherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _keys = options.Keys.ToDictionary(pair => pair.Key, pair => DecodeKey(pair.Key, pair.Value));
        if (!_keys.ContainsKey(options.ActiveKeyVersion))
        {
            throw new ArgumentException("The active encryption key version is not configured.", nameof(options));
        }

        _activeKeyVersion = options.ActiveKeyVersion;
    }

    public EncryptedValue Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_keys[_activeKeyVersion], TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, VersionBytes(_activeKeyVersion));

        return new EncryptedValue(
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            _activeKeyVersion);
    }

    public string Decrypt(EncryptedValue value)
    {
        if (!_keys.TryGetValue(value.KeyVersion, out var key))
        {
            throw new CryptographicException($"Encryption key version {value.KeyVersion} is unavailable.");
        }

        var ciphertext = Convert.FromBase64String(value.Ciphertext);
        var nonce = Convert.FromBase64String(value.Nonce);
        var tag = Convert.FromBase64String(value.Tag);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, VersionBytes(value.KeyVersion));

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DecodeKey(int version, string encodedKey)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"Encryption key version {version} is not valid Base64.", exception);
        }

        if (key.Length != KeySize)
        {
            throw new ArgumentException($"Encryption key version {version} must contain exactly 256 bits.");
        }

        return key;
    }

    private static byte[] VersionBytes(int version)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, version);
        return bytes;
    }
}
