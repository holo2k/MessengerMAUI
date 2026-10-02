using Messenger.Domain.Common;

namespace Messenger.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
