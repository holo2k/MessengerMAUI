using System.Security.Cryptography;
using Messenger.Application.Security;
using Messenger.Infrastructure.Security;

namespace Messenger.Infrastructure.Tests.Security;

public sealed class FieldCipherTests
{
    [Fact]
    public void Encrypt_then_decrypt_returns_original_plaintext()
    {
        var cipher = CreateCipher(7, Key(1));

        var encrypted = cipher.Encrypt("Привет, мир!");

        Assert.Equal("Привет, мир!", cipher.Decrypt(encrypted));
    }

    [Fact]
    public void Equal_plaintext_uses_distinct_nonces_and_ciphertexts()
    {
        var cipher = CreateCipher(7, Key(1));

        var first = cipher.Encrypt("same");
        var second = cipher.Encrypt("same");

        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
    }

    [Fact]
    public void Decrypt_with_a_different_key_rejects_the_value()
    {
        var encrypted = CreateCipher(7, Key(1)).Encrypt("secret");
        var wrongCipher = CreateCipher(7, Key(2));

        Assert.Throws<AuthenticationTagMismatchException>(() => wrongCipher.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypt_with_a_tampered_tag_rejects_the_value()
    {
        var cipher = CreateCipher(7, Key(1));
        var encrypted = cipher.Encrypt("secret");
        var tag = Convert.FromBase64String(encrypted.Tag);
        tag[0] ^= 0xff;
        var tampered = encrypted with { Tag = Convert.ToBase64String(tag) };

        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(tampered));
    }

    [Fact]
    public void Encrypted_value_preserves_the_active_key_version()
    {
        var encrypted = CreateCipher(42, Key(1)).Encrypt("secret");

        Assert.Equal(42, encrypted.KeyVersion);
    }

    [Fact]
    public void Cipher_rejects_keys_that_are_not_256_bits()
    {
        var options = new FieldCipherOptions
        {
            ActiveKeyVersion = 1,
            Keys = new Dictionary<int, string> { [1] = Convert.ToBase64String(new byte[16]) }
        };

        Assert.Throws<ArgumentException>(() => new AesGcmFieldCipher(options));
    }

    [Fact]
    public void Blind_index_is_deterministic_and_value_sensitive()
    {
        var index = new HmacBlindIndex(new BlindIndexOptions
        {
            Key = Convert.ToBase64String(Key(9))
        });

        var first = index.Compute("+79991234567");

        Assert.Equal(first, index.Compute("+79991234567"));
        Assert.NotEqual(first, index.Compute("+79991234568"));
    }

    private static AesGcmFieldCipher CreateCipher(int version, byte[] key) =>
        new(new FieldCipherOptions
        {
            ActiveKeyVersion = version,
            Keys = new Dictionary<int, string> { [version] = Convert.ToBase64String(key) }
        });

    private static byte[] Key(byte value) => Enumerable.Repeat(value, 32).ToArray();
}
