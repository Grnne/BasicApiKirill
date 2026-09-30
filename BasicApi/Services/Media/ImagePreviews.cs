using SkiaSharp;

namespace BasicApi.Services.Media;

/// <summary>A decoded picture: its size as it is shown (EXIF rotation applied) and a JPEG preview.</summary>
public sealed record ImagePreview(int Width, int Height, byte[] Jpeg, int PreviewWidth, int PreviewHeight);

/// <summary>
/// Previews of pictures, made on the server. The preview is re-encoded from pixels, so nothing of
/// the original file (metadata, a payload glued to the end) gets into it.
/// </summary>
public static class ImagePreviews
{
    public const int JpegQuality = 80;

    /// <summary>
    /// A preview whose longer side is at most <paramref name="maxSide"/>; null when the bytes are
    /// not a picture Skia can decode or it has more than <paramref name="maxPixels"/> pixels.
    /// </summary>
    public static ImagePreview? Make(byte[] data, int maxSide, long maxPixels)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(data));
        if (codec is null)
            return null;

        var info = codec.Info;
        // The header states the size before anything is decoded: a small file claiming a huge
        // picture (a decompression bomb) stops here.
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > maxPixels)
            return null;

        // JPEG decodes straight to a smaller size, much cheaper than full size and then down.
        var scale = Math.Min(1f, (float)maxSide / Math.Max(info.Width, info.Height));
        var decoded = scale < 1f ? codec.GetScaledDimensions(scale) : info.Size;
        using var bitmap = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            return null;

        var origin = codec.EncodedOrigin;
        var swaps = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (width, height) = swaps ? (info.Height, info.Width) : (info.Width, info.Height);

        // The preview in the source orientation, then turned as the camera said.
        var w = Math.Max(1, (int)Math.Round(info.Width * scale));
        var h = Math.Max(1, (int)Math.Round(info.Height * scale));
        var (pw, ph) = swaps ? (h, w) : (w, h);

        using var surface = SKSurface.Create(new SKImageInfo(pw, ph, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White); // JPEG has no transparency
        Orient(canvas, origin, w, h);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, new SKRect(0, 0, w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var jpeg = snapshot.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return new ImagePreview(width, height, jpeg.ToArray(), pw, ph);
    }

    /// <summary>
    /// Maps a picture of <paramref name="w"/>×<paramref name="h"/> drawn at the origin onto the
    /// canvas turned as EXIF orientation says. The last call applies to the point first.
    /// </summary>
    private static void Orient(SKCanvas canvas, SKEncodedOrigin origin, int w, int h)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // mirrored
                canvas.Translate(w, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // upside down
                canvas.Translate(w, h);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft: // flipped
                canvas.Translate(0, h);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // transposed: 90° clockwise, then mirrored
                canvas.Translate(h, 0);
                canvas.Scale(-1, 1);
                canvas.Translate(h, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightTop: // 90° clockwise
                canvas.Translate(h, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // 90° clockwise, then flipped
                canvas.Translate(0, w);
                canvas.Scale(1, -1);
                canvas.Translate(h, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.LeftBottom: // 90° counterclockwise
                canvas.Translate(0, w);
                canvas.RotateDegrees(-90);
                break;
        }
    }
}
