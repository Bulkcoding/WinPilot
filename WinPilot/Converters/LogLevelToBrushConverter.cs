using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WinPilot.Models;

namespace WinPilot.Converters;

public class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Error => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                LogLevel.Warning => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                _ => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
            };
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
