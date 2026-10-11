using Messenger.Contracts.Music;
using Microsoft.Maui.Controls.Shapes;

namespace Messenger.Maui.Controls;

public sealed class MusicArtworkView : ContentView
{
    public static readonly BindableProperty TrackProperty = BindableProperty.Create(nameof(Track), typeof(MusicTrackResponse), typeof(MusicArtworkView), null,
        propertyChanged: (view, _, _) => ((MusicArtworkView)view).UpdateArtwork());
    private readonly Border _artwork;
    private readonly Label _monogram;
    public MusicTrackResponse? Track { get => (MusicTrackResponse?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    public MusicArtworkView()
    {
        InputTransparent = true;
        var composition = new Grid { IsClippedToBounds = true };
        composition.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb("#19FFFFFF")), StrokeThickness = 0, Margin = new Thickness(-90, -45, 40, 45) });
        composition.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb("#25FFFFFF")), StrokeThickness = 1, Margin = new Thickness(25) });
        composition.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb("#25FFFFFF")), StrokeThickness = 1, Margin = new Thickness(42) });
        composition.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb("#25FFFFFF")), StrokeThickness = 1, Margin = new Thickness(59) });
        _monogram = new Label { TextColor = Colors.White, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        composition.Add(_monogram);
        _artwork = new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = composition };
        Content = _artwork;
        SizeChanged += (_, _) => _monogram.FontSize = Math.Clamp(Width * 0.24, 15, 80);
        UpdateArtwork();
    }
    private void UpdateArtwork()
    {
        var name = Track?.Title ?? "Музыка";
        var palettes = new[] { ("#357D68", "#102A36"), ("#76558B", "#202745"), ("#BC7A52", "#5C304F"), ("#4D86A6", "#283456"), ("#AD536F", "#432E60") };
        var colors = palettes[name.Aggregate(0, (hash, c) => (hash + c) % palettes.Length)];
        _artwork.Background = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(colors.Item1), 0), new(Color.FromArgb(colors.Item2), 1)
        }, new Point(0, 0), new Point(1, 1));
        _monogram.Text = DisplayValueConverter.Initials(name);
        SemanticProperties.SetDescription(this, $"Обложка: {name}");
    }
}
