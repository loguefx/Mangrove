using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;

namespace Mangrove.Server.Readers;

/// <summary>
/// Rasterizes PDF pages to images via Docnet (PDFium) so the existing image reader can display
/// them (spec §10 PDF reader). Docnet's native engine is not thread-safe, so all access is
/// serialized through <see cref="Gate"/>.
/// </summary>
public sealed class PdfPageReader
{
    private static readonly object Gate = new();
    private const double RenderScale = 2.0; // ~144 DPI — readable without huge payloads

    public int CountPages(byte[] pdf)
    {
        lock (Gate)
        {
            using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(1.0));
            return reader.GetPageCount();
        }
    }

    public (byte[] Bytes, string ContentType)? RenderPage(byte[] pdf, int index)
    {
        lock (Gate)
        {
            using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(RenderScale));
            if (index < 0 || index >= reader.GetPageCount()) return null;

            using var page = reader.GetPageReader(index);
            var width = page.GetPageWidth();
            var height = page.GetPageHeight();
            var raw = page.GetImage(); // BGRA, width*height*4

            using var pageBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            Marshal.Copy(raw, 0, pageBitmap.GetPixels(), Math.Min(raw.Length, width * height * 4));

            // Composite onto white so transparent PDF backgrounds don't turn black in JPEG.
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(pageBitmap, 0, 0);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            return (data.ToArray(), "image/jpeg");
        }
    }

    public byte[]? RenderCover(byte[] pdf) => RenderPage(pdf, 0)?.Bytes;
}
