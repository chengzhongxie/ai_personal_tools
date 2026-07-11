namespace PersonalAssistant.Infrastructure.Common.Helpers;

/// <summary>
/// 应用数据路径统一管理。避免 20+ 处重复拼接 AppData 路径。
/// </summary>
public static class AppPaths
{
    public static readonly string Root = System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant");

    // 目录
    public static string PluginsDir => System.IO.Path.Combine(Root, "Plugins");
    public static string WorkflowsDir => System.IO.Path.Combine(Root, "workflows");
    public static string SchedulesDir => System.IO.Path.Combine(Root, "schedules");
    public static string ModelsDir => System.IO.Path.Combine(Root, "models");
    public static string LogsDir => System.IO.Path.Combine(Root, "logs");
    public static string ConversationsDir => System.IO.Path.Combine(Root, "conversations");
    public static string KnowledgeBaseDir => System.IO.Path.Combine(Root, "knowledge_base");

    // 文件
    public static string SettingsFile => System.IO.Path.Combine(Root, "settings.json");
    public static string TokenUsageFile => System.IO.Path.Combine(Root, "token_usage.json");
    public static string PluginStateFile => System.IO.Path.Combine(Root, "plugin_state.json");
    public static string WidgetConfigFile => System.IO.Path.Combine(Root, "widget_config.json");
    public static string TodosFile => System.IO.Path.Combine(Root, "todos.json");
    public static string StickyNoteFile => System.IO.Path.Combine(Root, "sticky_note.txt");
    public static string DraftFile => System.IO.Path.Combine(Root, "draft.txt");
    public static string ChatHistoryFile => System.IO.Path.Combine(Root, "chat_history.json");
    public static string KnowledgeBaseIndexFile => System.IO.Path.Combine(KnowledgeBaseDir, "index.json");
    public static string ConversationIndexFile => System.IO.Path.Combine(ConversationsDir, "index.json");
    public static string ModelSourcesFile => "Assets/model_sources.json"; // 相对路径，运行时解析

    public static void EnsureDirectories()
    {
        System.IO.Directory.CreateDirectory(Root);
        System.IO.Directory.CreateDirectory(PluginsDir);
        System.IO.Directory.CreateDirectory(WorkflowsDir);
        System.IO.Directory.CreateDirectory(SchedulesDir);
        System.IO.Directory.CreateDirectory(ModelsDir);
        System.IO.Directory.CreateDirectory(LogsDir);
        System.IO.Directory.CreateDirectory(ConversationsDir);
        System.IO.Directory.CreateDirectory(KnowledgeBaseDir);
    }
}
