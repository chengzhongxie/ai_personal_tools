using System.IO;
using System.Windows;
using System.Windows.Input;

namespace PersonalAssistant.Features.Clipboard.Views;

/// <summary>
/// 快速便签窗口：临时记点东西，自动保存到 %APPDATA%。
/// 资源成本：仅在显示时消耗（窗口渲染），隐藏时零开销。
/// </summary>
public partial class StickyNoteWindow : Window
{
    private static readonly string NoteFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "sticky_note.txt");

    public StickyNoteWindow()
    {
        InitializeComponent();
        LoadNote();
        NoteTextBox.TextChanged += (_, _) => SaveNote();
    }

    private void LoadNote()
    {
        try
        {
            if (File.Exists(NoteFilePath))
                NoteTextBox.Text = File.ReadAllText(NoteFilePath);
        }
        catch { }
    }

    private void SaveNote()
    {
        try
        {
            File.WriteAllText(NoteFilePath, NoteTextBox.Text);
        }
        catch { }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        NoteTextBox.Clear();
        SaveNote();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SaveNote();
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
