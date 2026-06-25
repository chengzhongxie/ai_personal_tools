using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PersonalAssistant.Infrastructure.Common.Controls;

/// <summary>
/// 快捷键捕获控件：点击后捕获用户按下的组合键并显示。
/// 使用方式：绑定 CapturedModifiers 和 CapturedKey 属性。
/// </summary>
public class HotkeyCaptureBox : TextBox
{
    /// <summary>捕获的组合键修饰符</summary>
    public static readonly DependencyProperty CapturedModifiersProperty =
        DependencyProperty.Register(nameof(CapturedModifiers), typeof(ModifierKeys),
            typeof(HotkeyCaptureBox), new FrameworkPropertyMetadata(ModifierKeys.None,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>捕获的键</summary>
    public static readonly DependencyProperty CapturedKeyProperty =
        DependencyProperty.Register(nameof(CapturedKey), typeof(Key),
            typeof(HotkeyCaptureBox), new FrameworkPropertyMetadata(Key.None,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>快捷键显示文本</summary>
    public static readonly DependencyProperty HotkeyTextProperty =
        DependencyProperty.Register(nameof(HotkeyText), typeof(string),
            typeof(HotkeyCaptureBox), new PropertyMetadata("按组合键..."));

    public ModifierKeys CapturedModifiers
    {
        get => (ModifierKeys)GetValue(CapturedModifiersProperty);
        set => SetValue(CapturedModifiersProperty, value);
    }

    public Key CapturedKey
    {
        get => (Key)GetValue(CapturedKeyProperty);
        set => SetValue(CapturedKeyProperty, value);
    }

    public string HotkeyText
    {
        get => (string)GetValue(HotkeyTextProperty);
        set => SetValue(HotkeyTextProperty, value);
    }

    public HotkeyCaptureBox()
    {
        IsReadOnly = true;
        Text = "按组合键...";
        Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x80));
        Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x1A, 0x1F, 0x2E));
        BorderBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x2A, 0x30, 0x40));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8, 6, 8, 6);
        VerticalContentAlignment = VerticalAlignment.Center;
        FontSize = 13;
        GotFocus += OnGotFocus;
        LostFocus += OnLostFocus;
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        Text = "按下组合键...";
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        UpdateHotkeyText();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 忽略单独的修饰键（Alt/Ctrl/Shift/Win）
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.System)
            return;

        CapturedModifiers = Keyboard.Modifiers;
        CapturedKey = key;
        UpdateHotkeyText();

        // 移动焦点到下一个控件
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private void UpdateHotkeyText()
    {
        if (CapturedKey == Key.None)
        {
            Text = "未设置";
            HotkeyText = "未设置";
            return;
        }

        var parts = new System.Text.StringBuilder();
        var mods = CapturedModifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Append("Ctrl+");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Append("Alt+");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Append("Shift+");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Append("Win+");

        // 美化键名显示
        var keyName = CapturedKey switch
        {
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PgUp",
            Key.PageDown => "PgDn",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Tab => "Tab",
            Key.Capital => "CapsLock",
            Key.OemQuestion => "/",
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemTilde => "`",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemQuotes => "\"",
            Key.OemSemicolon => ";",
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            _ => CapturedKey.ToString()
        };

        parts.Append(keyName);
        Text = parts.ToString();
        HotkeyText = parts.ToString();
    }
}
