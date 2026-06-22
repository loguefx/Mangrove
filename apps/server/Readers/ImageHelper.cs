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
            if (original is null) return input;

            var scale = maxWidth / (double)original.Width;
            if (scale >= 1.0)
                return Encode(original, quality);

            var width = maxWidth;
            var height = Math.Max(1, (int)Math.Round(original.Height * scale));
            using var resized = original.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);
            if (resized is null) return Encode(original, quality);
            return Encode(resized, quality);
        }
        catch
        {
            return input;
        }
    }

    private static byte[] Encode(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }
}
