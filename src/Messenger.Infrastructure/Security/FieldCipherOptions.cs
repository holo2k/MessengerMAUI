namespace Messenger.Infrastructure.Security;

public sealed class FieldCipherOptions
{
    public int ActiveKeyVersion { get; init; }

    public IReadOnlyDictionary<int, string> Keys { get; init; } =
        new Dictionary<int, string>();
}
