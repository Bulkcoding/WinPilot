using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WinPilot.ViewModels;

namespace WinPilot.Converters;

public class StepStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is StepStatus status)
        {
            return status switch
            {
                StepStatus.Running => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                StepStatus.Completed => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                StepStatus.Failed => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                _ => new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63))
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
