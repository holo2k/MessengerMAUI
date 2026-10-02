namespace Messenger.Application.Security;

public interface IBlindIndex
{
    string Compute(string normalizedValue);
}
