namespace Messenger.Application.Security;

public readonly record struct EncryptedValue(
    string Ciphertext,
    string Nonce,
    string Tag,
    int KeyVersion);

public interface IFieldCipher
{
    EncryptedValue Encrypt(string plaintext);

    string Decrypt(EncryptedValue value);
}
