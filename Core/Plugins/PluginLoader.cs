using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Serilog;

namespace PersonalAssistant.Core.Plugins;

/// <summary>
/// 外部插件加载器：扫描 %APPDATA%\PersonalAssistant\Plugins\*.cs，
/// 使用 Roslyn 编译到内存，反射发现 PluginBase 子类并实例化。
/// 使用 AssemblyLoadContext 隔离加载，避免污染默认 Assembly 上下文（支持未来热卸载）。
/// 资源成本：启动时一次性 ~50-200ms CPU（Roslyn 编译），用完 GC。之后零开销。
/// </summary>
public class PluginLoader
{
    private static readonly string PluginsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "Plugins");

    // 跟踪每个文件的 ALC，供热重载时卸载
    private readonly Dictionary<string, AssemblyLoadContext> _alcMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 编译并加载 Plugins 目录下所有 .cs 文件中的 PluginBase 子类。
    /// 编译失败的插件会被跳过（warning 日志），不影响其他插件和应用正常运行。
    /// </summary>
    public List<PluginBase> LoadPlugins()
    {
        var result = new List<PluginBase>();

        if (!Directory.Exists(PluginsDir))
        {
            Directory.CreateDirectory(PluginsDir);
            Log.Information("[PluginLoader] 已创建插件目录: {Dir}", PluginsDir);
            return result;
        }

        var csFiles = Directory.GetFiles(PluginsDir, "*.cs");
        if (csFiles.Length == 0)
            return result;

        Log.Information("[PluginLoader] 发现 {Count} 个 .cs 文件，开始编译", csFiles.Length);

        var references = GetCompilationReferences();

        foreach (var file in csFiles)
        {
            try
            {
                var pluginBases = CompileAndLoad(file, references);
                foreach (var pb in pluginBases)
                {
                    pb.SourceFilePath = file;
                    var toolDefs = pb.GetToolDefinitions();
                    Log.Information("[PluginLoader] 插件 {Name} 加载成功: {Count} 个工具",
                        pb.Name, toolDefs.Length);
                }
                result.AddRange(pluginBases);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[PluginLoader] 跳过编译失败的插件文件: {File}", Path.GetFileName(file));
            }
        }

        Log.Information("[PluginLoader] 加载完成: {Count} 个外部插件", result.Count);
        return result;
    }

    /// <summary>
    /// 热重载单个插件文件：卸载旧 ALC → 重新编译 → 返回新的 PluginBase 实例。
    /// 如果编译失败，保留旧实例并返回 null。
    /// </summary>
    /// <param name="filePath">插件 .cs 文件的完整路径</param>
    /// <returns>新的 PluginBase 实例（失败返回 null）</returns>
    public PluginBase? ReloadPlugin(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        Log.Information("[PluginLoader] 热重载插件: {File}", fileName);

        try
        {
            // 卸载旧 ALC
            if (_alcMap.TryGetValue(filePath, out var oldAlc))
            {
                _alcMap.Remove(filePath);
                // ALC 的卸载是异步的，通过 GC 触发
                oldAlc.Unload();
            }

            // 重新编译
            var references = GetCompilationReferences();
            var pluginBases = CompileAndLoad(filePath, references);
            var result = pluginBases.FirstOrDefault();
            if (result is null)
            {
                Log.Warning("[PluginLoader] 热重载失败: {File} 中未找到 PluginBase 子类", fileName);
                return null;
            }

            result.SourceFilePath = filePath;
            Log.Information("[PluginLoader] 热重载成功: {Name}", result.Name);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PluginLoader] 热重载失败: {File}", fileName);
            return null;
        }
    }

    /// <summary>
    /// 编译单个 .cs 文件，反射获取所有 PluginBase 子类实例。
    /// 使用 AssemblyLoadContext 隔离加载，跟踪 ALC 供热重载时卸载。
    /// </summary>
    private List<PluginBase> CompileAndLoad(string filePath,
        List<MetadataReference> references)
    {
        var source = File.ReadAllText(filePath);
        var fileName = Path.GetFileName(filePath);

        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions()
            .WithLanguageVersion(LanguageVersion.Latest));

        var compilation = CSharpCompilation.Create(
            $"Plugin_{Path.GetFileNameWithoutExtension(fileName)}",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = string.Join("\n", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"编译失败:\n{errors}");
        }

        // 对于警告，记录日志但不阻止
        var warnings = emitResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning);
        foreach (var w in warnings)
            Log.Debug("[PluginLoader] {File}: {Warning}", fileName, w);

        ms.Seek(0, SeekOrigin.Begin);
        var alc = new AssemblyLoadContext(
            $"Plugin_{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.UtcNow:HHmmssfff}",
            isCollectible: true);
        var assembly = alc.LoadFromStream(ms);

        // 跟踪 ALC
        _alcMap[filePath] = alc;

        return assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(PluginBase).IsAssignableFrom(t))
            .Select(t =>
            {
                var instance = Activator.CreateInstance(t);
                if (instance is null)
                    throw new InvalidOperationException(
                        $"无法实例化插件类型: {t.FullName}");
                return (PluginBase)instance;
            })
            .ToList();
    }

    /// <summary>
    /// 收集 Roslyn 编译所需的程序集引用。
    /// 包括 core runtime 程序集 + PersonalAssistant.exe 自身（含 PluginBase 定义）。
    /// </summary>
    private static List<MetadataReference> GetCompilationReferences()
    {
        var refs = new List<MetadataReference>();

        // 添加 PersonalAssistant.exe 自身（让插件代码能引用 PluginBase）
        // 单文件发布时 Assembly.Location 为空，回退到 Environment.ProcessPath
        var exePath = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrEmpty(exePath))
            exePath = Environment.ProcessPath!;
        if (!string.IsNullOrEmpty(exePath))
            refs.Add(MetadataReference.CreateFromFile(exePath));

        // 添加所有已加载的运行时程序集
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                continue;

            try
            {
                refs.Add(MetadataReference.CreateFromFile(asm.Location));
            }
            catch
            {
                // 某些程序集可能无法加载（如动态生成的），跳过
            }
        }

        // 确保 System.Text.Json 和 System.Threading.Tasks 可用
        var systemAssemblies = new[]
        {
            typeof(object).Assembly,                    // System.Private.CoreLib
            typeof(System.Text.Json.JsonDocument).Assembly, // System.Text.Json
            typeof(System.Threading.Tasks.Task).Assembly,   // System.Threading.Tasks
            typeof(System.ComponentModel.DescriptionAttribute).Assembly, // System.ComponentModel.Primitives
            typeof(System.Linq.Enumerable).Assembly,         // System.Linq
        };

        foreach (var asm in systemAssemblies)
        {
            var path = asm.Location;
            if (!string.IsNullOrEmpty(path) && refs.All(r => r.Display != path))
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        return refs;
    }
}
