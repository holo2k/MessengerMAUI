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
        _monogram = new Label { TextColor = Colors.White, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        composition.Add(_monogram);
        _artwork = new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = composition };
        Content = _artwork;
        SizeChanged += (_, _) =>
        {
            _monogram.FontSize = Math.Clamp(Width * 0.24, 12, 64);
            _artwork.StrokeShape = new RoundRectangle { CornerRadius = Math.Clamp(Width * 0.08, 5, 18) };
        };
        UpdateArtwork();
    }
    private void UpdateArtwork()
    {
        var name = Track?.Title ?? "Музыка";
        var palettes = new[] { "#357D68", "#76558B", "#BC7A52", "#4D86A6", "#AD536F" };
        var color = palettes[name.Aggregate(0, (hash, c) => (hash + c) % palettes.Length)];
        _artwork.Background = new SolidColorBrush(Color.FromArgb(color));
        _monogram.Text = DisplayValueConverter.Initials(name);
        SemanticProperties.SetDescription(this, $"Обложка: {name}");
    }
}
