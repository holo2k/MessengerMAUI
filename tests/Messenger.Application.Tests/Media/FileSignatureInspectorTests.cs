using Messenger.Application.Media;
using Messenger.Domain.Media;

namespace Messenger.Application.Tests.Media;

public sealed class FileSignatureInspectorTests
{
    [Theory]
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 0 })]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0 })]
    [InlineData(new byte[] { 0xFF, 0xF3, 0x80, 0 })]
    public void Mp3_with_or_without_id3_tags_is_accepted(byte[] header) =>
        new FileSignatureInspector().Validate("track.mp3", "audio/mpeg", "audio/mpeg", 1000, header);

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFF, 0x90, 0 })]
    [InlineData(new byte[] { 0xFF, 0xEB, 0x90, 0 })]
    [InlineData(new byte[] { 0xFF, 0xFB, 0xFC, 0 })]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90 })]
    public void Invalid_mp3_frame_is_rejected(byte[] header) =>
        Assert.Throws<MediaException>(() => new FileSignatureInspector().Validate("track.mp3", "audio/mpeg", "audio/mpeg", 1000, header));
}
