using System.Text;
using BasicApi.Features.Media;
using BasicApi.Storage.Entities;
using SkiaSharp;

namespace BasicApi.Tests.Features.Media;

public class MediaSnifferTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, MediaSniffer.Jpeg)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, MediaSniffer.Png)]
    [InlineData(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1 }, MediaSniffer.Webm)]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0x00 }, MediaSniffer.Mpeg)]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, MediaSniffer.Unknown)] // "<svg"
    [InlineData(new byte[0], MediaSniffer.Unknown)]
    public void Sniff_FindsTheTypeByTheFirstBytes(byte[] head, string expected) =>
        Assert.Equal(expected, MediaSniffer.Sniff(head));

    [Theory]
    [InlineData("GIF89a....", MediaSniffer.Gif)]
    [InlineData("RIFF\0\0\0\0WEBPVP8 ", MediaSniffer.Webp)]
    [InlineData("RIFF\0\0\0\0WAVEfmt ", MediaSniffer.Unknown)]
    [InlineData("OggS\0\u0002", MediaSniffer.Ogg)]
    [InlineData("ID3\u0004\0", MediaSniffer.Mpeg)]
    [InlineData("\0\0\0\u0018ftypisom", MediaSniffer.Mp4)]
    [InlineData("\0\0\0\u0014ftypqt  ", MediaSniffer.QuickTime)]
    [InlineData("\0\0\0\u0020ftypM4A ", MediaSniffer.M4a)]
    [InlineData("<html><body>", MediaSniffer.Unknown)]
    public void Sniff_TextSignatures(string head, string expected) =>
        Assert.Equal(expected, MediaSniffer.Sniff(Encoding.Latin1.GetBytes(head)));
}

public class ImagePreviewsTests
{
    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.White);
        // A red top-left corner: shows where the picture ended up after rotation.
        using (var canvas = new SKCanvas(bitmap))
            canvas.DrawRect(0, 0, width / 4f, height / 4f, new SKPaint { Color = SKColors.Red });
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray();
    }

    /// <summary>The JPEG with an EXIF block saying how the camera was held.</summary>
    private static byte[] WithOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] tiff =
        [
            (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, // little-endian TIFF, IFD at 8
            1, 0,                                     // one entry
            0x12, 0x01, 3, 0, 1, 0, 0, 0,             // Orientation, SHORT, count 1
            (byte)orientation, 0, 0, 0,               // value
            0, 0, 0, 0                                // no next IFD
        ];
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var length = payload.Length + 2;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)length];
        return [.. jpeg[..2], .. app1, .. payload, .. jpeg[2..]];
    }

    [Fact]
    public void Preview_KeepsTheShape_AndFitsTheLongerSide()
    {
        var preview = ImagePreviews.Make(Jpeg(2000, 1000), 640, 50_000_000)!;

        Assert.Equal((2000, 1000), (preview.Width, preview.Height));
        Assert.Equal((640, 320), (preview.PreviewWidth, preview.PreviewHeight));
        using var decoded = SKBitmap.Decode(preview.Jpeg);
        Assert.Equal((640, 320), (decoded.Width, decoded.Height));
    }

    [Fact]
    public void SmallPicture_IsNotBlownUp()
    {
        var preview = ImagePreviews.Make(Jpeg(100, 50), 640, 50_000_000)!;

        Assert.Equal((100, 50), (preview.PreviewWidth, preview.PreviewHeight));
    }

    [Fact]
    public void CameraRotation_IsApplied_ToTheSizeAndThePreview()
    {
        // Held sideways: stored 400×200, shown 200×400, the stored top-left corner goes top-right.
        var preview = ImagePreviews.Make(WithOrientation(Jpeg(400, 200), 6), 640, 50_000_000)!;

        Assert.Equal((200, 400), (preview.Width, preview.Height));
        using var decoded = SKBitmap.Decode(preview.Jpeg);
        Assert.Equal((200, 400), (decoded.Width, decoded.Height));
        Assert.True(decoded.GetPixel(190, 10).Red > 200 && decoded.GetPixel(190, 10).Green < 80);
        Assert.True(decoded.GetPixel(10, 10).Green > 200);
    }

    [Theory]
    [InlineData(3, 390, 190)] // upside down: the red corner is bottom-right
    [InlineData(8, 10, 390)]  // turned the other way: bottom-left
    public void OtherRotations_PutTheCornerWhereTheViewerSeesIt(ushort orientation, int x, int y)
    {
        var preview = ImagePreviews.Make(WithOrientation(Jpeg(400, 200), orientation), 640, 50_000_000)!;
        using var decoded = SKBitmap.Decode(preview.Jpeg);

        var pixel = decoded.GetPixel(x, y);
        Assert.True(pixel.Red > 200 && pixel.Green < 80, $"{pixel} at {x},{y} of {decoded.Width}x{decoded.Height}");
    }

    [Fact]
    public void TooManyPixels_OrNotAPicture_GiveNoPreview()
    {
        Assert.Null(ImagePreviews.Make(Jpeg(300, 300), 640, maxPixels: 80_000));
        Assert.Null(ImagePreviews.Make("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), 640, 50_000_000));
    }
}

public class MediaServingTests
{
    [Theory]
    [InlineData("photo.jpg", "photo.jpg")]
    [InlineData("C:\\Users\\me\\photo.jpg", "photo.jpg")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("a\u0000b\r\n.txt", "ab.txt")]
    [InlineData("  ", "file")]
    [InlineData("..", "file")]
    [InlineData(null, "file")]
    public void FileName_IsJustAName(string? name, string expected) =>
        Assert.Equal(expected, MediaService.CleanFileName(name));

    [Fact]
    public void Files_AreDownloads_OfUnknownType_WithTheirNameKept()
    {
        var file = new Attachment { Kind = AttachmentKinds.File, Mime = "text/html", FileName = "отчёт.html" };

        Assert.Equal(MediaSniffer.Unknown, MediaService.ServedType(file));
        Assert.Equal("attachment; filename=\"_____.html\"; filename*=UTF-8''%D0%BE%D1%82%D1%87%D1%91%D1%82.html",
            MediaService.Disposition(file));
    }

    [Fact]
    public void Photos_AreShown_WithTheTypeTheServerFound()
    {
        var photo = new Attachment { Kind = AttachmentKinds.Photo, Mime = MediaSniffer.Png, FileName = "a.png" };

        Assert.Equal(MediaSniffer.Png, MediaService.ServedType(photo));
        Assert.Equal("inline", MediaService.Disposition(photo));
    }
}
