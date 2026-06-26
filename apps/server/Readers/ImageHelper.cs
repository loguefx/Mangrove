using SkiaSharp;

namespace Mangrove.Server.Readers;

/// <summary>Thumbnail/cover resizing via SkiaSharp (spec §2). Falls back to the original bytes
/// if decoding fails (e.g. an exotic format), so a cover is always produced.</summary>
public static class ImageHelper
{
    public static byte[] ResizeCover(byte[] input, int maxWidth = 512, int quality = 80)
    {
        try
        {
            using var original = SKBitmap.Decode(input);
            if (original is null || original.Width <= 0 || original.Height <= 0)
                return input;

            // Never upscale; only shrink covers wider than the target.
            var scale = Math.Min(1.0, maxWidth / (double)original.Width);
            var width = Math.Max(1, (int)Math.Round(original.Width * scale));
            var height = Math.Max(1, (int)Math.Round(original.Height * scale));

            var encoded = Render(original, width, height, quality);
            // Guard against a degenerate/empty encode: keep the original bytes instead.
            return encoded.Length > 0 ? encoded : input;
        }
        catch
        {
            return input;
        }
    }

    /// <summary>
    /// Renders the source onto an opaque surface and encodes to JPEG. Flattening against a solid
    /// background is important because some source covers (e.g. PNG/WebP from online providers) carry
    /// an alpha channel — JPEG has no alpha, so transparent pixels would otherwise encode as solid
    /// black. Drawing through a surface also avoids quirks where a resized bitmap encodes blank.
    /// </summary>
    private static byte[] Render(SKBitmap source, int width, int height, int quality)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null) return Array.Empty<byte>();

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using (var srcImage = SKImage.FromBitmap(source))
        {
            var dest = new SKRect(0, 0, width, height);
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            canvas.DrawImage(srcImage, dest, sampling);
        }
        canvas.Flush();

        using var outImage = surface.Snapshot();
        using var data = outImage.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data?.ToArray() ?? Array.Empty<byte>();
    }
}
