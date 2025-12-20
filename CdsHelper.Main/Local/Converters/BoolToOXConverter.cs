using System.Globalization;
using System.Windows.Data;

namespace CdsHelper.Main.Local.Converters;

/// <summary>
/// true이면 O, false이면 X
/// </summary>
public class BoolToOXConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? "O" : "X";
        return "X";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
            return s == "O";
        return false;
    }
}
