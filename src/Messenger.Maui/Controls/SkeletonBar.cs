using Microsoft.Maui.Controls.Shapes;

namespace Messenger.Maui.Controls;

public sealed class SkeletonBar : Border
{
    public SkeletonBar()
    {
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = 4 };
        BackgroundColor = Color.FromArgb("#52655E");
        WidthRequest = 42; HeightRequest = 10;
        VerticalOptions = LayoutOptions.Center;
        InputTransparent = true;
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => this.AbortAnimation("SkeletonPulse");
    }
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsVisible)) UpdateAnimation();
    }
    private void UpdateAnimation()
    {
        this.AbortAnimation("SkeletonPulse");
        if (!IsVisible || Handler is null) return;
        new Animation(progress => Opacity = 0.45 + 0.4 * Math.Sin(progress * Math.PI))
            .Commit(this, "SkeletonPulse", 16, 1200, repeat: () => IsVisible && Handler is not null);
    }
}
