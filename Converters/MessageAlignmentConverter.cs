using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PersonalAssistant.Features.Chat.Models.Enums;

namespace PersonalAssistant.Converters;

public class MessageAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MessageRole role)
        {
            return role switch
            {
                MessageRole.User => HorizontalAlignment.Right,
                MessageRole.Assistant => HorizontalAlignment.Left,
                _ => HorizontalAlignment.Center
            };
        }
        return HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
