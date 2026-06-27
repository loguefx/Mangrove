using Mangrove.Server.Readers;
using SkiaSharp;
using Xunit;

namespace Mangrove.Server.Tests;

public class ImageHelperTests
{
    // A cover with transparency must not encode to a solid-black JPEG.
    [Fact]
    public void ResizeCover_FlattensTransparentPng_NotBlack()
    {
        var png = MakeTransparentPng(800, 1200);
        var outBytes = ImageHelper.ResizeCover(png);

        var avg = AverageBrightness(outBytes);
        Assert.True(avg > 40, $"expected non-black cover, average brightness was {avg}");
    }

    [Fact]
    public void IsBannerAspect_FlagsWideBannerNotPortrait()
    {
        var banner = MakeSolidColorJpeg(1200, 240, new SKColor(10, 10, 10));
        var poster = MakeSolidColorJpeg(800, 1280, new SKColor(10, 10, 10));
        Assert.True(ImageHelper.IsBannerAspect(banner), "wide banner should be flagged");
        Assert.False(ImageHelper.IsBannerAspect(poster), "portrait poster should not be flagged");
    }

    [Fact]
    public void ResizeCover_KeepsOpaqueColorImage()
    {
        var jpg = MakeSolidColorJpeg(800, 1200, new SKColor(40, 120, 200));
        var outBytes = ImageHelper.ResizeCover(jpg);

        var avg = AverageBrightness(outBytes);
        Assert.True(avg > 40, $"expected non-black cover, average brightness was {avg}");
    }

    private static byte[] MakeTransparentPng(int w, int h)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = new SKColor(220, 60, 60), IsAntialias = true };
            canvas.DrawCircle(w / 2f, h / 2f, w / 4f, paint);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] MakeSolidColorJpeg(int w, int h, SKColor color)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var bmp = new SKBitmap(info);
        using (var canvas = new SKCanvas(bmp))
            canvas.Clear(color);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    private static double AverageBrightness(byte[] jpeg)
    {
        using var bmp = SKBitmap.Decode(jpeg);
        Assert.NotNull(bmp);
        double sum = 0;
        int n = 0;
        for (int y = 0; y < bmp!.Height; y += 17)
        for (int x = 0; x < bmp.Width; x += 17)
        {
            var c = bmp.GetPixel(x, y);
            sum += (c.Red + c.Green + c.Blue) / 3.0;
            n++;
        }
        return n == 0 ? 0 : sum / n;
    }
}
