using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PersonalAssistant.Features.Widgets;

public partial class TodoWidget : UserControl
{
    private static readonly string TodoPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "todos.json");

    private readonly ObservableCollection<TodoItem> _items = new();

    public TodoWidget()
    {
        InitializeComponent();
        TodoList.ItemsSource = _items;
        Load();
    }

    private void Load()
    {
        if (!File.Exists(TodoPath)) return;
        try
        {
            var json = File.ReadAllText(TodoPath);
            var items = JsonSerializer.Deserialize<List<TodoItem>>(json) ?? new();
            foreach (var item in items)
                _items.Add(item);
        }
        catch { }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(TodoPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(_items.ToList());
        File.WriteAllText(TodoPath, json);
    }

    private void NewTodoBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddTodo();
    }

    private void AddTodo_Click(object sender, RoutedEventArgs e) => AddTodo();

    private void AddTodo()
    {
        var text = NewTodoBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var item = new TodoItem { Text = text, IsDone = false };
        _items.Insert(0, item);
        NewTodoBox.Text = "";
        Save();
    }

    private class TodoItem
    {
        public string Text { get; set; } = "";
        public bool IsDone { get; set; }
    }
}
