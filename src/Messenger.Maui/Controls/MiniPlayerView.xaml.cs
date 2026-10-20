using Messenger.Maui.Features.Music;
using Microsoft.Maui.Controls.Shapes;

namespace Messenger.Maui.Controls;

public partial class MiniPlayerView : ContentView
{
    private readonly MusicViewModel _viewModel;
    private readonly Func<Task> _openPlayer;

    public MiniPlayerView(MusicViewModel viewModel, Func<Task> openPlayer)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _openPlayer = openPlayer;
    }

    public void SetCompact(bool compact)
    {
        HeightRequest = compact ? 56 : 60;
        PlayerSurface.StrokeShape = new RoundRectangle { CornerRadius = compact ? 0 : 14 };
        PlayerSurface.StrokeThickness = compact ? 0 : 1;
        PlayerBody.Padding = compact ? new Thickness(12, 4) : new Thickness(8, 6);
        var artworkSize = compact ? 30 : 36;
        Artwork.WidthRequest = Artwork.HeightRequest = artworkSize;
        TrackDetails.ColumnDefinitions[0].Width = new GridLength(artworkSize);
        TrackTitle.FontSize = compact ? 13 : 14;
        TrackArtist.FontSize = compact ? 11 : 12;
    }

    private async void OnOpenPlayer(object? sender, TappedEventArgs e) => await _openPlayer();

    private async void OnPauseClicked(object? sender, EventArgs e)
    {
        if (_viewModel.HasPlaybackError) await _viewModel.RetryAsync();
        else _viewModel.TogglePause();
    }

    private async void OnNextClicked(object? sender, EventArgs e) => await _viewModel.NextAsync();
}
