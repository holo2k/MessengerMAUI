using Messenger.Contracts.Media;
using Messenger.Maui.Services;

namespace Messenger.Maui.Controls;

public sealed class MessageAttachmentView : ContentView
{
    public static readonly BindableProperty AttachmentProperty = BindableProperty.Create(nameof(Attachment), typeof(StoredObjectResponse), typeof(MessageAttachmentView), null,
        propertyChanged: (view, _, _) => ((MessageAttachmentView)view).Reload());
    private static readonly SemaphoreSlim ImageSlots = new(3, 3);
    private readonly Image _image = new() { Aspect = Aspect.AspectFit, InputTransparent = true };
    private readonly Label _status = new() { FontSize = 13, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, InputTransparent = true };
    private readonly ActivityIndicator _loading = new() { Color = Color.FromArgb("#0088FF"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
    private readonly SkeletonView _skeleton = new() { Kind = SkeletonKind.Image, IsVisible = false };
    private CancellationTokenSource? _load;
    private byte[]? _bytes;
    private bool _opening;
    private ImageDimensions? _dimensions;
    public static readonly BindableProperty ThumbnailSizeProperty = BindableProperty.Create(nameof(ThumbnailSize), typeof(double), typeof(MessageAttachmentView), 0d,
        propertyChanged: (view, _, _) => ((MessageAttachmentView)view).UpdateDisplaySize());
    public double ThumbnailSize { get => (double)GetValue(ThumbnailSizeProperty); set => SetValue(ThumbnailSizeProperty, value); }
    public static readonly BindableProperty MaxImageWidthProperty = BindableProperty.Create(nameof(MaxImageWidth), typeof(double), typeof(MessageAttachmentView), 280d,
        propertyChanged: (view, _, _) => ((MessageAttachmentView)view).UpdateDisplaySize());
    public double MaxImageWidth { get => (double)GetValue(MaxImageWidthProperty); set => SetValue(MaxImageWidthProperty, value); }
    public event EventHandler? DisplaySizeChanged;
    public StoredObjectResponse? Attachment { get => (StoredObjectResponse?)GetValue(AttachmentProperty); set => SetValue(AttachmentProperty, value); }
    private bool IsImage => Attachment?.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
    private bool HasPreview => IsImage || Attachment?.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true;
    private IMessageAttachmentClient? Client => Handler?.MauiContext?.Services.GetService(typeof(IMessageAttachmentClient)) as IMessageAttachmentClient;

    public MessageAttachmentView()
    {
        var grid = new Grid { IsClippedToBounds = true, SafeAreaEdges = SafeAreaEdges.None, BackgroundColor = Color.FromArgb("#E8EDF2") };
        grid.Add(_image);
        grid.Add(_status);
        grid.Add(_skeleton);
        grid.Add(_loading);
        Content = grid;
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);
        HandlerChanged += (_, _) => Reload();
        Loaded += (_, _) => { if (_image.Source is null && (_load is null || _load.IsCancellationRequested)) Reload(); };
        Unloaded += (_, _) => { _load?.Cancel(); _image.Source = null; _bytes = null; };
        HorizontalOptions = LayoutOptions.Center;
    }

    private void Reload()
    {
        _load?.Cancel();
        _load?.Dispose();
        _load = null;
        _bytes = null;
        _dimensions = null;
        _image.Source = null;
        _status.IsVisible = true;
        _loading.IsRunning = _loading.IsVisible = false;
        _skeleton.IsVisible = false;
        if (Attachment is not { } attachment) return;
        _skeleton.IsVisible = HasPreview;
        _status.IsVisible = !HasPreview;
        UpdateDisplaySize();
        _status.Text = attachment.FileName;
        SemanticProperties.SetDescription(this, attachment.FileName);
        if (!HasPreview || Client is not { } client) return;
        _load = new();
        _ = LoadImageAsync(client, attachment, _load.Token);
    }

    private async Task LoadImageAsync(IMessageAttachmentClient client, StoredObjectResponse attachment, CancellationToken ct)
    {
        _loading.IsRunning = true;
        try
        {
            await ImageSlots.WaitAsync(ct);
            byte[] bytes;
            try
            {
                if (IsImage)
                {
                    using var buffer = new MemoryStream();
                    await client.DownloadAsync(attachment.Id, buffer, ct);
                    ct.ThrowIfCancellationRequested();
                    _bytes = bytes = buffer.ToArray();
                }
                else bytes = await VideoThumbnail.CreateAsync(attachment, client, ct);
            }
            finally { ImageSlots.Release(); }
            ct.ThrowIfCancellationRequested();
            _dimensions = ImageDimensions.Read(bytes);
            UpdateDisplaySize();
            _image.Source = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
            _status.IsVisible = false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!ct.IsCancellationRequested)
            {
                _bytes = null;
                _status.IsVisible = true;
                _status.Text = IsImage ? "Не удалось загрузить фото. Нажмите, чтобы повторить." : "▶ Видео";
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                _loading.IsRunning = _loading.IsVisible = false;
                _skeleton.IsVisible = false;
            }
        }
    }

    private void UpdateDisplaySize()
    {
        var width = Math.Max(1, MaxImageWidth);
        _image.Aspect = ThumbnailSize > 0 ? Aspect.AspectFill : Aspect.AspectFit;
        var size = ThumbnailSize > 0 ? new ImageDimensions(ThumbnailSize, ThumbnailSize)
            : HasPreview ? (_dimensions?.Fit(width, 420) ?? new ImageDimensions(width, width * 2 / 3)) : new ImageDimensions(width, 64);
        WidthRequest = size.Width;
        HeightRequest = size.Height;
        DisplaySizeChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_opening || Attachment is not { } attachment || Client is not { } client) return;
        if (IsImage && _bytes is null) { if (!_loading.IsRunning) Reload(); return; }
        _opening = true;
        try
        {
            await AttachmentFile.OpenAsync(attachment, client, _bytes);
        }
        catch (Exception exception) { await Shell.Current.DisplayAlertAsync("Вложение", exception.Message, "OK"); }
        finally { _opening = false; }
    }
}
