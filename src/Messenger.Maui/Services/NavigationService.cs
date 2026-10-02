namespace Messenger.Maui.Services;

public interface INavigationService
{
    Task ShowAuthenticatedAsync();
    Task ShowLoginAsync();
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public sealed class NavigationService(IServiceProvider services) : INavigationService
{
    public Task ShowAuthenticatedAsync() => SetRootAsync(
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<AppShell>(services));

    public Task ShowLoginAsync() => SetRootAsync(
        new NavigationPage(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Features.Auth.LoginPage>(services)));

    private static Task SetRootAsync(Page page) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        var window = Application.Current?.Windows.FirstOrDefault()
            ?? throw new InvalidOperationException("The application window is not ready.");
        window.Page = page;
    });
}
#endif
