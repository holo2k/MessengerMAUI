using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.Content;
using AndroidX.Core.View;
using AndroidX.Core.View.Insets;

namespace Messenger.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Window?.DecorView is not ViewGroup decorView) return;

        var statusBarBackground = new ProtectionLayout(this)
        {
            Clickable = false,
            Focusable = false,
            ImportantForAccessibility = ImportantForAccessibility.NoHideDescendants,
            Elevation = 16 * (Resources?.DisplayMetrics?.Density ?? 1)
        };
        statusBarBackground.SetProtections([
            new ColorProtection(WindowInsetsCompat.Side.Top,
                ContextCompat.GetColor(this, Resource.Color.system_bar_background))
        ]);
        decorView.AddView(statusBarBackground, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        var insetsController = WindowCompat.GetInsetsController(Window, decorView);
        if (insetsController is not null) insetsController.AppearanceLightStatusBars = true;
    }
}
