using Messenger.Maui.Services;

namespace Messenger.Maui.Controls;

public sealed class GalleryThumbnailView : ContentView
{
    public static readonly BindableProperty ItemProperty = BindableProperty.Create(nameof(Item), typeof(GalleryMediaItem), typeof(GalleryThumbnailView), null,
        propertyChanged: (view, _, _) => ((GalleryThumbnailView)view).Reload());
    private static readonly SemaphoreSlim ThumbnailSlots = new(4, 4);
    private readonly Image _image = new() { Aspect = Aspect.AspectFill };
    private readonly SkeletonView _skeleton = new() { Kind = SkeletonKind.Image };
    private CancellationTokenSource? _load;
    public GalleryMediaItem? Item { get => (GalleryMediaItem?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }

    public GalleryThumbnailView()
    {
        InputTransparent = true;
        IsClippedToBounds = true;
        BackgroundColor = Color.FromArgb("#E8EDF2");
        var grid = new Grid();
        grid.Add(_image);
        grid.Add(_skeleton);
        Content = grid;
        HandlerChanged += (_, _) => Reload();
    }

    private void Reload()
    {
        _load?.Cancel();
        _load?.Dispose();
        _image.Source = null;
        _skeleton.IsVisible = Item is not null;
        if (Item is not { } item || Handler?.MauiContext?.Services.GetService(typeof(IGalleryService)) is not IGalleryService gallery) return;
        _load = new();
        _ = LoadAsync(gallery, item, _load.Token);
    }

    private async Task LoadAsync(IGalleryService gallery, GalleryMediaItem item, CancellationToken ct)
    {
        try
        {
            await ThumbnailSlots.WaitAsync(ct);
            byte[]? bytes;
            try { bytes = await gallery.GetThumbnailAsync(item, 320, ct); }
            finally { ThumbnailSlots.Release(); }
            if (!ct.IsCancellationRequested)
            {
                if (bytes is not null) _image.Source = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
                _skeleton.IsVisible = false;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!ct.IsCancellationRequested) _skeleton.IsVisible = false;
            System.Diagnostics.Debug.WriteLine($"Gallery thumbnail: {exception.Message}");
        }
    }
}
