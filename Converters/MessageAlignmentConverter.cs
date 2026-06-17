using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PersonalAssistant.Features.Chat.Models.Enums;

namespace PersonalAssistant.Converters;

/// <summary>
/// 消息角色到水平对齐方式的转换器：User → Right，Assistant → Left，其他 → Center
/// </summary>
public class MessageAlignmentConverter : IValueConverter
{
    /// <summary>MessageRole → HorizontalAlignment</summary>
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

    /// <summary>不支持反向转换</summary>
    /// <exception cref="NotSupportedException">始终抛出</exception>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
