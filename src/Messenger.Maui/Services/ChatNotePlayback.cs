namespace Messenger.Maui.Services;

public static class ChatNotePlayback
{
    public static event Action<object?>? ActiveChanged;
    public static void Activate(object? owner) => ActiveChanged?.Invoke(owner);
    public static void StopAll() => Activate(null);
}
