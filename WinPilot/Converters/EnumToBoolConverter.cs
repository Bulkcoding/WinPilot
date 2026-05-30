using System.Globalization;
using System.Windows.Data;

namespace WinPilot.Converters;

/// RadioButton ↔ Enum 바인딩용
/// IsChecked="{Binding SelectedType, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=Path}"
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
            return Enum.Parse(targetType, parameter.ToString()!);
        return Binding.DoNothing;
    }
}
