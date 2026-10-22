namespace Messenger.Maui.Services;

public readonly record struct ImageDimensions(double Width, double Height)
{
    public static ImageDimensions FromPixels(double width, double height, int orientation = 1) =>
        orientation is >= 5 and <= 8 ? new(height, width) : new(width, height);

    public ImageDimensions Fit(double maxWidth, double maxHeight)
    {
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width <= 0 || Height <= 0 ||
            !double.IsFinite(maxWidth) || !double.IsFinite(maxHeight) || maxWidth <= 0 || maxHeight <= 0)
            throw new InvalidDataException("Не удалось определить размеры изображения.");
        var scale = Math.Min(1, Math.Min(maxWidth / Width, maxHeight / Height));
        return new(Width * scale, Height * scale);
    }

#if ANDROID || IOS
    public static ImageDimensions Read(byte[] bytes)
    {
#if ANDROID
        using var options = new global::Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
        var orientation = 1;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var exif = new AndroidX.ExifInterface.Media.ExifInterface(stream);
            orientation = exif.GetAttributeInt(AndroidX.ExifInterface.Media.ExifInterface.TagOrientation, 1);
        }
        catch (Java.IO.IOException) { }
        return FromPixels(options.OutWidth, options.OutHeight, orientation);
#else
        using var data = Foundation.NSData.FromArray(bytes);
        var options = new ImageIO.CGImageOptions { ShouldCache = false };
        using var source = ImageIO.CGImageSource.FromData(data, options)
            ?? throw new InvalidDataException("Не удалось прочитать изображение.");
        var properties = source.GetProperties(0, options);
        return FromPixels(properties.PixelWidth ?? 0, properties.PixelHeight ?? 0, (int)(properties.Orientation ?? (CoreImage.CIImageOrientation)1));
#endif
    }
#endif
}
