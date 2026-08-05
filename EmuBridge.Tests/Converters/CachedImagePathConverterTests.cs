using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EmuBridge.Converters;

namespace EmuBridge.Tests.Converters;

public class CachedImagePathConverterTests : IDisposable
{
    private readonly string _path;
    private readonly CachedImagePathConverter _converter = new();

    public CachedImagePathConverterTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"emubridge_cachedimage_test_{Guid.NewGuid()}.png");
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static void WriteSolidColorPng(string path, Color color)
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(color), null, new System.Windows.Rect(0, 0, 8, 8));
        }

        var renderTarget = new RenderTargetBitmap(8, 8, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static Color ReadTopLeftPixel(BitmapSource source)
    {
        var frame = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = frame.PixelWidth * 4;
        var pixels = new byte[frame.PixelHeight * stride];
        frame.CopyPixels(pixels, stride, 0);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    [Fact]
    public void Convert_NullOrEmptyPath_ReturnsNull()
    {
        Assert.Null(_converter.Convert(null!, typeof(object), null!, null!));
        Assert.Null(_converter.Convert(string.Empty, typeof(object), null!, null!));
    }

    [Fact]
    public void Convert_ValidPath_ReturnsFrozenBitmapImage()
    {
        WriteSolidColorPng(_path, Colors.Red);

        var result = _converter.Convert(_path, typeof(object), null!, null!);

        var bitmap = Assert.IsType<BitmapImage>(result);
        Assert.True(bitmap.IsFrozen);
    }

    [Fact]
    public void Convert_FileRewrittenAtSamePathAfterFirstLoad_SecondLoadReflectsNewContent()
    {
        // Reproduces the real bug (ARCHITECTURE.md -> ADR-23 Update): WPF's own process-level
        // bitmap cache, keyed by URI, can keep serving the first-ever-loaded content for a given
        // path even after the file on disk is deleted and rewritten with different bytes — the
        // exact scenario a "Remove from Library" followed by the same game reappearing in a
        // rescan would hit. CachedImagePathConverter must bypass that cache entirely.
        WriteSolidColorPng(_path, Colors.Red);
        var first = (BitmapImage)_converter.Convert(_path, typeof(object), null!, null!)!;
        Assert.Equal(Colors.Red, ReadTopLeftPixel(first));

        File.Delete(_path);
        WriteSolidColorPng(_path, Colors.Blue);
        var second = (BitmapImage)_converter.Convert(_path, typeof(object), null!, null!)!;

        Assert.Equal(Colors.Blue, ReadTopLeftPixel(second));
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(null!, typeof(object), null!, null!));
    }
}
