using Messenger.Contracts.Music;
using Microsoft.Maui.Controls.Shapes;
using Messenger.Maui.Services;

namespace Messenger.Maui.Controls;

public sealed class MusicArtworkView : ContentView
{
    public static readonly BindableProperty TrackProperty = BindableProperty.Create(nameof(Track), typeof(MusicTrackResponse), typeof(MusicArtworkView), null,
        propertyChanged: (view, _, _) => ((MusicArtworkView)view).UpdateArtwork());
    private readonly Border _artwork;
    private readonly Label _monogram;
    private readonly Image _image;
    private readonly SkeletonView _skeleton = new() { Kind = SkeletonKind.Image, IsVisible = false };
    private int _loadVersion;
    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(MusicArtworkView), 6d,
        propertyChanged: (view, _, value) => ((MusicArtworkView)view)._artwork.StrokeShape = new RoundRectangle { CornerRadius = (double)value });
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public MusicTrackResponse? Track { get => (MusicTrackResponse?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    public MusicArtworkView()
    {
        InputTransparent = true;
        var composition = new Grid { IsClippedToBounds = true };
        _monogram = new Label { TextColor = Colors.White, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        composition.Add(_monogram);
        _image = new Image { Aspect = Aspect.AspectFill, IsVisible = false };
        composition.Add(_image);
        composition.Add(_skeleton);
        _artwork = new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = CornerRadius }, Content = composition };
        Content = _artwork;
        SizeChanged += (_, _) =>
        {
            _monogram.FontSize = Math.Clamp(Width * 0.24, 12, 64);
        };
        HandlerChanged += (_, _) => _ = LoadCoverAsync();
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
        _ = LoadCoverAsync();
    }

    private async Task LoadCoverAsync()
    {
        var version = ++_loadVersion;
        _image.Source = null;
        _image.IsVisible = false;
        var hasCover = Track?.CoverObjectId is not null;
        _monogram.IsVisible = !hasCover;
        _skeleton.IsVisible = hasCover;
        if (Track?.CoverObjectId is not { } coverId ||
            Handler?.MauiContext?.Services.GetService(typeof(IAvatarImageClient)) is not IAvatarImageClient images) return;
        try
        {
            var bytes = await images.GetAsync(coverId.ToString());
            if (version != _loadVersion) return;
            if (bytes is null) { _skeleton.IsVisible = false; _monogram.IsVisible = true; return; }
            _image.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            _image.IsVisible = true;
            _monogram.IsVisible = false;
            _skeleton.IsVisible = false;
        }
        catch (Exception exception)
        {
            if (version == _loadVersion) { _skeleton.IsVisible = false; _monogram.IsVisible = true; }
            System.Diagnostics.Debug.WriteLine($"Track cover: {exception.Message}");
        }
    }
}
