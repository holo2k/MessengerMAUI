using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Maui;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new NavigationPage(_services.GetRequiredService<Features.Auth.LoginPage>()));
}
