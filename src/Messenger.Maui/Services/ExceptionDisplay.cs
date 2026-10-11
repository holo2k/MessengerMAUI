namespace Messenger.Maui.Services;

public static class ExceptionDisplay
{
    public static string Format(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var outer = Describe(exception);
        var root = exception.GetBaseException();
        return ReferenceEquals(root, exception)
            ? outer
            : $"{outer}{Environment.NewLine}Причина: {Describe(root)}";
    }

    private static string Describe(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}";
}
