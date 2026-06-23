using System.IO;
using System.Net.Http;
using System.Text.Json;
using LLama;
using LLama.Common;
using Serilog;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// 本地小模型推理服务，使用 LLamaSharp 加载 Qwen2.5-0.5B-Instruct GGUF。
/// 模型获取优先级：%APPDATA% 已有 → 打包目录复制 → 自动下载（多镜像回退）。
/// 资源成本：首次加载 ~550MB 内存（模型 + KV Cache），空闲时仅内存驻留。
/// </summary>
public sealed class LocalModelService : IDisposable
{
    private readonly string _modelPath;
    private readonly string _bundledModelPath;
    private readonly string _modelsDir;
    private readonly ModelDownloadConfig _downloadConfig;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private LLamaWeights? _weights;
    private LLamaContext? _context;
    private bool _disposed;

    public LocalModelService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _modelsDir = Path.Combine(appData, "PersonalAssistant", "models");
        _modelPath = Path.Combine(_modelsDir, ModelDownloadConfig.ModelFileName);

        // 打包时随程序发布的模型路径
        _bundledModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "Assets", ModelDownloadConfig.ModelFileName);

        // 加载下载配置（打包时随程序发布，URL 可独立更新无需改代码）
        _downloadConfig = ModelDownloadConfig.Load();
    }

    // ──── 公开属性 ────

    /// <summary>模型是否已就绪（文件存在且已加载到内存）</summary>
    public bool IsReady => _context is not null;

    /// <summary>
    /// 确保模型文件存在（自动部署/下载），返回 null 表示成功，否则返回错误信息。
    /// 供 UI 层提前调用以展示"正在准备模型..."。
    /// </summary>
    public async Task<string?> EnsureModelAvailableAsync(
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // 1. %APPDATA% 已有 → 直接返回
        if (File.Exists(_modelPath))
            return null;

        // 2. 打包目录有 → 复制到 %APPDATA%
        if (File.Exists(_bundledModelPath))
        {
            try
            {
                progress?.Report("正在部署本地模型...");
                Directory.CreateDirectory(_modelsDir);
                File.Copy(_bundledModelPath, _modelPath, overwrite: false);
                Log.Information("[LocalModel] 已从打包目录部署模型");
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[LocalModel] 部署失败，尝试下载");
            }
        }

        // 3. 自动下载（多镜像回退）
        if (_downloadConfig.Mirrors.Count == 0)
        {
            return $"模型文件未找到，且未配置下载地址。\n" +
                   $"请将模型文件放入 {_modelsDir} 目录。";
        }

        var errors = new List<string>();
        for (var i = 0; i < _downloadConfig.Mirrors.Count; i++)
        {
            var url = _downloadConfig.Mirrors[i];
            try
            {
                progress?.Report($"正在下载本地模型... (源 {i + 1}/{_downloadConfig.Mirrors.Count})");
                Log.Information("[LocalModel] 开始下载模型: {Url}", url);

                Directory.CreateDirectory(_modelsDir);
                var tmpPath = _modelPath + ".download";

                using var response = await _http.GetAsync(url,
                    HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write);

                var buffer = new byte[8192];
                var downloaded = 0L;
                int read;
                var lastReport = 0;

                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;

                    // 每 10% 报告一次进度
                    if (totalBytes > 0)
                    {
                        var pct = (int)(downloaded * 100 / totalBytes);
                        if (pct - lastReport >= 10)
                        {
                            lastReport = pct;
                            progress?.Report($"正在下载本地模型... {pct}%");
                        }
                    }
                }

                File.Move(tmpPath, _modelPath); // 原子完成（Windows 上同卷即为原子重命名）
                Log.Information("[LocalModel] 下载完成: {Size}MB",
                    new FileInfo(_modelPath).Length / (1024 * 1024));
                return null;
            }
            catch (OperationCanceledException)
            {
                CleanupTemp();
                return "下载已取消。";
            }
            catch (Exception ex)
            {
                var msg = $"镜像 {i + 1} 失败: {ex.Message}";
                errors.Add(msg);
                Log.Warning(ex, "[LocalModel] 下载失败 [{Index}/{Total}]: {Url}",
                    i + 1, _downloadConfig.Mirrors.Count, url);
                CleanupTemp();
            }
        }

        return $"自动下载失败，所有镜像不可达：\n\n{string.Join("\n", errors)}\n\n" +
               $"请手动下载模型文件放入：{_modelsDir}";
    }

    /// <summary>
    /// 向本地模型发送 prompt 并获取回复（单轮无状态）。
    /// 延迟初始化：首次调用时加载模型到内存。
    /// </summary>
    /// <param name="prompt">用户输入文本</param>
    /// <param name="maxTokens">最大生成 token 数，默认 256</param>
    /// <param name="systemPrompt">系统提示词，默认通用助手</param>
    /// <param name="ct">取消令牌</param>
    public async Task<string> InferAsync(string prompt,
        int maxTokens = 256,
        string systemPrompt = "You are a helpful assistant. Answer concisely.",
        CancellationToken ct = default)
    {
        if (_disposed)
            return "本地模型服务已释放。";

        // 确保模型文件可用（自动部署/下载）
        var availabilityError = await EnsureModelAvailableAsync(ct: ct);
        if (availabilityError is not null)
            return availabilityError;

        // 加载模型到内存
        try
        {
            await EnsureInitializedAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "本地模型初始化失败");
            return $"本地模型初始化失败：{ex.Message}";
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_context is null)
                return "本地模型未正确初始化。";

            var executor = new InteractiveExecutor(_context);

            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = ["<|im_end|>", "<|endoftext|>"],
            };

            var fullPrompt = $"<|im_start|>system\n{systemPrompt}<|im_end|>\n" +
                            $"<|im_start|>user\n{prompt}<|im_end|>\n" +
                            $"<|im_start|>assistant\n";

            var output = new System.Text.StringBuilder();
            await foreach (var token in executor.InferAsync(fullPrompt, inferenceParams, ct))
            {
                output.Append(token);
            }

            var result = output.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "(模型返回为空)" : result;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_context is not null)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_context is not null)
                return;

            Log.Information("[LocalModel] 正在加载模型: {Path}", _modelPath);

            var parameters = new ModelParams(_modelPath)
            {
                ContextSize = 1024,
                GpuLayerCount = 0,
            };

            _weights = LLamaWeights.LoadFromFile(parameters);
            _context = _weights.CreateContext(parameters);

            Log.Information("[LocalModel] 模型加载完成，内存 ~550MB");
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void CleanupTemp()
    {
        try { File.Delete(_modelPath + ".download"); }
        catch (Exception ex) { Log.Debug(ex, "[LocalModel] 清理临时下载文件失败"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _context?.Dispose();
        _weights?.Dispose();
        _initLock?.Dispose();
        _http.Dispose();
    }
}

/// <summary>
/// 模型下载配置 DTO，从 Assets/model_sources.json 加载。
/// 该文件随程序打包发布，可独立更新无需重新编译代码。
/// </summary>
internal sealed class ModelDownloadConfig
{
    public const string ModelFileName = "qwen2.5-0.5b-instruct-q4_k_m.gguf";
    public const string ConfigFileName = "model_sources.json";

    /// <summary>镜像列表，按优先级排列（先试第一个）</summary>
    public List<string> Mirrors { get; init; } = [];

    public static ModelDownloadConfig Load()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "Assets", ConfigFileName);

        try
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 读取默认模型名，查找对应的 mirrors
                var defaultModel = root.GetProperty("default_model").GetString() ?? ModelFileName;

                if (root.TryGetProperty("models", out var models) &&
                    models.TryGetProperty(defaultModel, out var modelEntry) &&
                    modelEntry.TryGetProperty("mirrors", out var mirrorsArray))
                {
                    var mirrors = new List<string>();
                    foreach (var m in mirrorsArray.EnumerateArray())
                    {
                        var url = m.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                            mirrors.Add(url);
                    }

                    if (mirrors.Count > 0)
                        return new ModelDownloadConfig { Mirrors = mirrors };
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[LocalModel] 加载下载配置失败: {Path}", configPath);
        }

        // 兜底：配置文件缺失或格式错误 → 返回空列表，提示用户手动放置模型
        return new ModelDownloadConfig { Mirrors = [] };
    }
}
