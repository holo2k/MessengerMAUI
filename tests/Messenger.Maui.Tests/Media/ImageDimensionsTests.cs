using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Media;

public sealed class ImageDimensionsTests
{
    [Theory]
    [InlineData(1800, 1200, 280, 186.6666667)]
    [InlineData(1200, 1800, 280, 420)]
    [InlineData(1080, 1920, 236.25, 420)]
    [InlineData(1800, 200, 280, 31.1111111)]
    [InlineData(64, 48, 64, 48)]
    public void Photo_frame_follows_the_image_ratio_and_fits_inside_the_message(double width, double height, double expectedWidth, double expectedHeight)
    {
        var size = ImageDimensions.FromPixels(width, height).Fit(280, 420);
        Assert.Equal(expectedWidth, size.Width, precision: 5);
        Assert.Equal(expectedHeight, size.Height, precision: 5);
        Assert.Equal(width / height, size.Width / size.Height, precision: 8);
    }

    [Theory]
    [InlineData(1, 1800, 1200)]
    [InlineData(3, 1800, 1200)]
    [InlineData(5, 1200, 1800)]
    [InlineData(6, 1200, 1800)]
    [InlineData(7, 1200, 1800)]
    [InlineData(8, 1200, 1800)]
    public void Camera_orientation_is_applied_before_calculating_the_frame(int orientation, double width, double height)
    {
        Assert.Equal(new ImageDimensions(width, height), ImageDimensions.FromPixels(1800, 1200, orientation));
    }
}
