using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Bridge.Converters;

// Explicitly bypasses WPF's own process-level bitmap cache (keyed by URI, separate from
// ImageCacheService's on-disk file cache) — a plain `Source="{Binding PathString}"` binding lets
// WPF's implicit string->ImageSource conversion use its default caching, which can keep serving an
// old decoded bitmap after the file at that same path has been deleted and rewritten with new
// content (see DEVELOPMENT.md -> Image Loading, ARCHITECTURE.md -> ADR-23 Update).
public class CachedImagePathConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
        {
            return null;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
