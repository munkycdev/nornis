using SkiaSharp;

namespace Nornis.Infrastructure.Printing;

/// <summary>
/// Fits cover art to the space a layout gives it (feature 38): centre-cropped to that space's
/// shape, so the art fills it without stretching, scaled to print resolution and no further,
/// and re-encoded as JPEG so a booklet with art stays a sensible size to download and file.
/// </summary>
internal static class CoverArtImage
{
    /// <summary>Pixels per inch the art is kept at: sharp in print, never larger than it needs to be.</summary>
    internal const int PrintDpi = 200;

    private const int JpegQuality = 88;

    /// <summary>The art cropped and scaled for a box of <paramref name="widthPt"/> by <paramref name="heightPt"/>, or null when it can't be decoded.</summary>
    internal static byte[]? Fit(byte[] art, float widthPt, float heightPt)
    {
        using var bitmap = Decode(art);
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
        {
            return null;
        }

        var target = widthPt / heightPt;
        var (cropWidth, cropHeight) = (float)bitmap.Width / bitmap.Height > target
            ? ((int)Math.Round(bitmap.Height * target), bitmap.Height)
            : (bitmap.Width, (int)Math.Round(bitmap.Width / target));
        cropWidth = Math.Clamp(cropWidth, 1, bitmap.Width);
        cropHeight = Math.Clamp(cropHeight, 1, bitmap.Height);
        var source = SKRectI.Create((bitmap.Width - cropWidth) / 2, (bitmap.Height - cropHeight) / 2, cropWidth, cropHeight);

        // Down to print resolution, never up: enlarging adds bytes, not detail.
        var scale = Math.Min(1f, widthPt / 72f * PrintDpi / cropWidth);
        var outWidth = Math.Max(1, (int)Math.Round(cropWidth * scale));
        var outHeight = Math.Max(1, (int)Math.Round(cropHeight * scale));

        using var surface = SKSurface.Create(new SKImageInfo(outWidth, outHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        using (var image = SKImage.FromBitmap(bitmap))
        {
            surface.Canvas.DrawImage(image, source, SKRect.Create(outWidth, outHeight), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        using var snapshot = surface.Snapshot();
        using var jpeg = snapshot.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return jpeg.ToArray();
    }

    /// <summary>Null for bytes no codec reads: Skia throws for those rather than returning null.</summary>
    private static SKBitmap? Decode(byte[] art)
    {
        using var data = SKData.CreateCopy(art);
        using var codec = SKCodec.Create(data);
        return codec is null ? null : SKBitmap.Decode(codec);
    }
}
