using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CdsHelper.Main.Local.Converters;

/// <summary>
/// null이면 Collapsed, 아니면 Visible
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
