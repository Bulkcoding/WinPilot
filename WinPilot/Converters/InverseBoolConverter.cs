using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinPilot.Converters;

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool inverted = value is bool b && !b;
        // ConverterParameter=Visibility → bool 대신 Visibility 반환
        if (parameter is string s && s == "Visibility")
            return inverted ? Visibility.Visible : Visibility.Collapsed;
        return inverted;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
