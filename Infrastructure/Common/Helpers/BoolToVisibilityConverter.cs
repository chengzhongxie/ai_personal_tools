using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PersonalAssistant.Infrastructure.Common.Helpers;

/// <summary>
/// 布尔值到 Visibility 的转换器：true → Visible，false → Collapsed
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>bool → Visibility</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    /// <summary>Visibility → bool</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility v)
            return v == Visibility.Visible;
        return false;
    }
}
