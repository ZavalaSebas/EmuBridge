using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EmuBridge.Converters;

public class BoolToWindowStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? WindowState.Maximized : WindowState.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
