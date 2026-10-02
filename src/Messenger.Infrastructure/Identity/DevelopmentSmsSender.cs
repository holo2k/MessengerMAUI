using Messenger.Application.Identity;
using Messenger.Domain.Common;

namespace Messenger.Infrastructure.Identity;

public sealed class DevelopmentSmsSender : ISmsSender
{
    public Task SendCodeAsync(PhoneNumber phone, string code, CancellationToken cancellationToken)
    {
        if (code != "111111")
        {
            throw new InvalidOperationException("The development SMS sender only accepts code 111111.");
        }

        return Task.CompletedTask;
    }
}
