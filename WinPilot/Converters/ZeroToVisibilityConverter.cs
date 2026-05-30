using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinPilot.Converters;

/// 값이 0이면 Visible, 아니면 Collapsed (빈 목록 안내 텍스트용)
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
