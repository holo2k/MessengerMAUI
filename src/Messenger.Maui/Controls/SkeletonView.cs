using Microsoft.Maui.Controls.Shapes;

namespace Messenger.Maui.Controls;

public enum SkeletonKind { List, Chats, Music, Messages, Profile, Settings, Gallery, Image, Form }

public sealed class SkeletonView : ContentView
{
    public static readonly BindableProperty KindProperty = BindableProperty.Create(nameof(Kind), typeof(SkeletonKind), typeof(SkeletonView), SkeletonKind.List,
        propertyChanged: (view, _, _) => ((SkeletonView)view).Rebuild());
    private View? _shapes;
    private Page? _page;
    private bool _active = true;
    public SkeletonKind Kind { get => (SkeletonKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public SkeletonView()
    {
        InputTransparent = true;
        IsClippedToBounds = true;
        Rebuild();
        Loaded += OnLoaded;
        Unloaded += (_, _) => { this.AbortAnimation("ScreenSkeleton"); DetachPage(); };
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsVisible)) UpdateAnimation();
    }

    private static Border Bar(double height, double width = -1, double radius = 5) => new()
    {
        BackgroundColor = Color.FromArgb("#E5E8EC"), StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = radius }, HeightRequest = height, WidthRequest = width,
        HorizontalOptions = width < 0 ? LayoutOptions.Fill : LayoutOptions.Start, InputTransparent = true
    };

    private void Rebuild()
    {
        if (Kind == SkeletonKind.Image)
        {
            _shapes = new Border { BackgroundColor = Color.FromArgb("#E5E8EC"), StrokeThickness = 0 };
            Content = _shapes;
            return;
        }
        if (Kind == SkeletonKind.Gallery)
        {
            var tiles = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 2, RowSpacing = 2 };
            for (var row = 0; row < 4; row++)
            {
                tiles.RowDefinitions.Add(new RowDefinition(120));
                for (var column = 0; column < 3; column++)
                    tiles.Add(new Border { BackgroundColor = Color.FromArgb("#E5E8EC"), StrokeThickness = 0 }, column, row);
            }
            tiles.SizeChanged += (_, _) =>
            {
                if (tiles.Width > 4) foreach (var row in tiles.RowDefinitions) row.Height = new GridLength((tiles.Width - 4) / 3);
            };
            _shapes = tiles;
            Content = tiles;
            return;
        }
        var stack = new VerticalStackLayout { Spacing = 0 };
        if (Kind is SkeletonKind.Profile or SkeletonKind.Settings)
        {
            if (Kind == SkeletonKind.Profile)
            {
                var portrait = Bar(210, 210, 105);
                portrait.HorizontalOptions = LayoutOptions.Center;
                portrait.Margin = new Thickness(24);
                stack.Add(portrait);
            }
            else stack.Add(Row(64));
            stack.Add(new VerticalStackLayout { Padding = 16, Spacing = 14, Children = { Bar(18, 170), Bar(12, 115) } });
        }
        if (Kind == SkeletonKind.Form)
        {
            stack.Padding = new Thickness(24, 32);
            stack.Spacing = 24;
            stack.Add(Bar(18, 180));
            stack.Add(Bar(14));
            stack.Add(Bar(48));
            stack.Add(Bar(48));
        }
        else if (Kind == SkeletonKind.Messages)
        {
            for (var index = 0; index < 7; index++)
            {
                var bubble = Bar(index % 3 == 0 ? 78 : 56, index % 2 == 0 ? 220 : 175, 12);
                bubble.HorizontalOptions = index % 2 == 0 ? LayoutOptions.Start : LayoutOptions.End;
                bubble.Margin = new Thickness(16, 8);
                stack.Add(bubble);
            }
        }
        else
            for (var index = 0; index < 7; index++)
                stack.Add(Kind == SkeletonKind.Settings
                    ? new VerticalStackLayout { Padding = new Thickness(16, 20), Children = { Bar(16, index % 2 == 0 ? 180 : 120) } }
                    : Row(Kind == SkeletonKind.Music ? 44 : 52));
        _shapes = stack;
        Content = stack;
    }

    private Grid Row(double avatarSize)
    {
        var row = new Grid { ColumnDefinitions = [new(avatarSize), new(GridLength.Star), new(30)], ColumnSpacing = 12, Padding = new Thickness(16, 10) };
        row.Add(Bar(avatarSize, avatarSize, Kind == SkeletonKind.Music ? 6 : avatarSize / 2));
        var lines = new VerticalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center };
        lines.Add(Bar(14));
        lines.Add(Bar(11, 105));
        row.Add(lines, 1);
        row.Add(Bar(10, 28), 2);
        return row;
    }

    private void UpdateAnimation()
    {
        this.AbortAnimation("ScreenSkeleton");
        if (!IsVisible || !_active || Handler is null || _shapes is null) return;
        new Animation(progress => { if (_shapes is { } shapes) shapes.Opacity = 0.65 + 0.3 * Math.Sin(progress * Math.PI); })
            .Commit(this, "ScreenSkeleton", 32, 1300, repeat: () => IsVisible && _active && Handler is not null);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        DetachPage();
        for (Element? parent = Parent; parent is not null; parent = parent.Parent)
            if (parent is Page page) { _page = page; break; }
        if (_page is not null) { _page.Appearing += OnPageAppearing; _page.Disappearing += OnPageDisappearing; }
        _active = true;
        UpdateAnimation();
    }

    private void OnPageAppearing(object? sender, EventArgs e) { _active = true; UpdateAnimation(); }
    private void OnPageDisappearing(object? sender, EventArgs e) { _active = false; this.AbortAnimation("ScreenSkeleton"); }
    private void DetachPage()
    {
        if (_page is null) return;
        _page.Appearing -= OnPageAppearing;
        _page.Disappearing -= OnPageDisappearing;
        _page = null;
    }
}
