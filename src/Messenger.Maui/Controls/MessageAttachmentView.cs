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
    public StoredObjectResponse? Attachment { get => (StoredObjectResponse?)GetValue(AttachmentProperty); set => SetValue(AttachmentProperty, value); }
    private bool IsImage => Attachment?.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
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
        Loaded += (_, _) => { if (_bytes is null) Reload(); };
        Unloaded += (_, _) => { _load?.Cancel(); _image.Source = null; _bytes = null; };
        SizeChanged += (_, _) =>
        {
            if (IsImage && Width > 0 && Math.Abs(HeightRequest - Width) > 0.5) HeightRequest = Width;
        };
    }

    private void Reload()
    {
        _load?.Cancel();
        _load?.Dispose();
        _load = null;
        _bytes = null;
        _image.Source = null;
        _status.IsVisible = true;
        _loading.IsRunning = _loading.IsVisible = false;
        _skeleton.IsVisible = false;
        if (Attachment is not { } attachment) return;
        _skeleton.IsVisible = IsImage;
        _status.IsVisible = !IsImage;
        HeightRequest = IsImage ? (Width > 0 ? Width : 260) : 64;
        _status.Text = attachment.FileName;
        SemanticProperties.SetDescription(this, attachment.FileName);
        if (!IsImage || Client is not { } client) return;
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
                using var buffer = new MemoryStream();
                await client.DownloadAsync(attachment.Id, buffer, ct);
                ct.ThrowIfCancellationRequested();
                _bytes = bytes = buffer.ToArray();
            }
            finally { ImageSlots.Release(); }
            _image.Source = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
            _status.IsVisible = false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!ct.IsCancellationRequested)
            {
                _status.IsVisible = true;
                _status.Text = "Не удалось загрузить фото. Нажмите, чтобы повторить.";
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

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_opening || Attachment is not { } attachment || Client is not { } client) return;
        if (IsImage && _bytes is null) { if (!_loading.IsRunning) Reload(); return; }
        _opening = true;
        try
        {
            var folder = Path.Combine(FileSystem.CacheDirectory, "message-attachments", attachment.Id.ToString("N"));
            Directory.CreateDirectory(folder);
            var name = Path.GetFileName(attachment.FileName.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(name)) name = "attachment";
            var path = Path.Combine(folder, name);
            await using (var file = File.Create(path))
            {
                if (_bytes is { } bytes) await file.WriteAsync(bytes);
                else await client.DownloadAsync(attachment.Id, file);
            }
            await Launcher.Default.OpenAsync(new OpenFileRequest(attachment.FileName, new ReadOnlyFile(path, attachment.ContentType)));
        }
        catch (Exception exception) { await Shell.Current.DisplayAlertAsync("Вложение", exception.Message, "OK"); }
        finally { _opening = false; }
    }
}
