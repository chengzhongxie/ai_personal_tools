using System.Collections.ObjectModel;
using System.Windows;
using PersonalAssistant.Features.Chat.Models;

namespace PersonalAssistant.Features.Notifications;

/// <summary>
/// 通知历史面板窗口。显示最近 50 条系统通知。
/// 资源成本：仅打开时消耗，空闲时零开销（Transient 窗口）。
/// </summary>
public partial class NotificationHistoryWindow : Window
{
    public NotificationHistoryWindow(ObservableCollection<NotificationRecord> records)
    {
        InitializeComponent();

        if (records.Count == 0)
        {
            EmptyHint.Visibility = Visibility.Visible;
            NotificationListBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            NotificationListBox.ItemsSource = records;
            EmptyHint.Visibility = Visibility.Collapsed;
        }
    }
}
