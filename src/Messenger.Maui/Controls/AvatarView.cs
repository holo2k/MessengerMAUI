using Microsoft.Maui.Controls.Shapes;
using Messenger.Maui.Services;

namespace Messenger.Maui.Controls;

public sealed class AvatarView : ContentView
{
    public static readonly BindableProperty PersonProperty = BindableProperty.Create(
        nameof(Person), typeof(object), typeof(AvatarView), null, propertyChanged: OnPersonChanged);
    public static readonly BindableProperty AvatarObjectIdProperty = BindableProperty.Create(
        nameof(AvatarObjectId), typeof(string), typeof(AvatarView), null, propertyChanged: OnAvatarChanged);
    private readonly Label _initials;
    private readonly Image _image;
    private readonly Border _circle;
    private readonly SkeletonView _skeleton = new() { Kind = SkeletonKind.Image, IsVisible = false };
    private int _loadVersion;

    public AvatarView()
    {
        WidthRequest = HeightRequest = 52;
        _initials = new Label
        {
            FontSize = 18, FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center,
            TextColor = Color.FromArgb("#52778A"), Text = "?"
        };
        _image = new Image { Aspect = Aspect.AspectFill, IsVisible = false };
        var content = new Grid();
        content.Children.Add(_image);
        content.Children.Add(_initials);
        content.Children.Add(_skeleton);
        _circle = new Border
        {
            StrokeThickness = 0, StrokeShape = new Ellipse(),
            BackgroundColor = Color.FromArgb("#D9EAF2"), Content = content
        };
        Content = _circle;
        SizeChanged += (_, _) => _initials.FontSize = Math.Clamp(Width * 0.34, 12, 84);
        HandlerChanged += (_, _) => _ = LoadAvatarAsync();
    }

    public object? Person { get => GetValue(PersonProperty); set => SetValue(PersonProperty, value); }
    public string? AvatarObjectId { get => (string?)GetValue(AvatarObjectIdProperty); set => SetValue(AvatarObjectIdProperty, value); }

    private static void OnPersonChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (AvatarView)bindable;
        var name = DisplayValueConverter.Name(newValue);
        view._initials.Text = DisplayValueConverter.Initials(name);
        var palette = new[] { "#D9EAF2", "#EFE1D5", "#E2DDF2", "#DCEBDC", "#F1DDE2" };
        var index = name.Aggregate(0, (total, character) => (total + character) % palette.Length);
        view._circle.BackgroundColor = Color.FromArgb(palette[index]);
        SemanticProperties.SetDescription(view, name);
    }

    private static void OnAvatarChanged(BindableObject bindable, object oldValue, object newValue) =>
        _ = ((AvatarView)bindable).LoadAvatarAsync();

    private async Task LoadAvatarAsync()
    {
        var version = ++_loadVersion;
        var client = Handler?.MauiContext?.Services.GetService(typeof(IAvatarImageClient)) as IAvatarImageClient;
        if (string.IsNullOrWhiteSpace(AvatarObjectId))
        {
            ShowFallback(version);
            return;
        }

        _image.IsVisible = _initials.IsVisible = false;
        _skeleton.IsVisible = true;
        if (client is null) return;

        try
        {
            var bytes = await client.GetAsync(AvatarObjectId);
            if (version != _loadVersion)
            {
                return;
            }
            if (bytes is null)
            {
                ShowFallback(version);
                return;
            }

            _image.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            _image.IsVisible = true;
            _initials.IsVisible = false;
            _skeleton.IsVisible = false;
        }
        catch
        {
            ShowFallback(version);
        }
    }

    private void ShowFallback(int version)
    {
        if (version != _loadVersion)
        {
            return;
        }
        _image.Source = null;
        _image.IsVisible = false;
        _initials.IsVisible = true;
        _skeleton.IsVisible = false;
    }
}
