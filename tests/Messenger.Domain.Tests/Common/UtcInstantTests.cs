using Messenger.Domain.Common;

namespace Messenger.Domain.Tests.Common;

public sealed class UtcInstantTests
{
    [Fact]
    public void From_NormalizesOffsetToUtc()
    {
        var local = new DateTimeOffset(2026, 10, 2, 12, 30, 0, TimeSpan.FromHours(5));

        var instant = UtcInstant.From(local);

        Assert.Equal(TimeSpan.Zero, instant.Value.Offset);
        Assert.Equal(local, instant.Value);
    }
}
