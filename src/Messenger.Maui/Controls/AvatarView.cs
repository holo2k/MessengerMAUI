using Microsoft.Maui.Controls.Shapes;

namespace Messenger.Maui.Controls;

public sealed class AvatarView : ContentView
{
    public static readonly BindableProperty PersonProperty = BindableProperty.Create(
        nameof(Person), typeof(object), typeof(AvatarView), null, propertyChanged: OnPersonChanged);
    private readonly Label _initials;
    private readonly Border _circle;

    public AvatarView()
    {
        WidthRequest = HeightRequest = 52;
        _initials = new Label
        {
            FontSize = 18, FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center,
            TextColor = Color.FromArgb("#52778A"), Text = "?"
        };
        _circle = new Border
        {
            StrokeThickness = 0, StrokeShape = new Ellipse(),
            BackgroundColor = Color.FromArgb("#D9EAF2"), Content = _initials
        };
        Content = _circle;
        SizeChanged += (_, _) => _initials.FontSize = Math.Clamp(Width * 0.34, 12, 84);
    }

    public object? Person { get => GetValue(PersonProperty); set => SetValue(PersonProperty, value); }

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
}
