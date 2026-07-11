using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Core.Services;
using PersonalAssistant.Features.Chat.Models;
using PersonalAssistant.Features.Chat.Models.Enums;
using PersonalAssistant.Features.Chat.Services;
using PersonalAssistant.Infrastructure.Common.Services;
using Serilog;
using Wpf.Ui.Controls;

namespace PersonalAssistant.Features.Chat.ViewModels;

/// <summary>
/// 聊天界面的 ViewModel，管理消息列表、流式 AI 响应、/clear 命令和对话持久化
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private const int MaxDisplayMessages = 200;
    private const int MaxInputLength = 50000; // 最大输入长度限制（~12K tokens），防止超大粘贴导致 UI 冻结

    private readonly MessagePreprocessor _preprocessor;
    private readonly ChatAgentService _chatAgent;
    private readonly IChatHistoryService _historyService;
    private readonly ConversationStorageService _convStorage;
    private readonly ModelRoutingService _routing;
    private readonly TokenUsageService _tokenUsage;
    private readonly ConversationSummarizer _summarizer;
    private readonly PluginSharedState _sharedState;
    private readonly LocalCommandInterceptor _localCmd;

    // 输入历史（环形缓冲区）
    private const int MaxInputHistory = 50;
    private readonly List<string> _inputHistory = new(MaxInputHistory);
    private int _historyIndex = -1;

    // 取消令牌源（用于中止流式响应）
    private CancellationTokenSource? _currentCts;

    /// <summary>对话列表 ViewModel</summary>
    public ConversationListViewModel ConversationList { get; }

    /// <summary>聊天消息列表</summary>
    [ObservableProperty]
    private ObservableCollection<ChatMessage> _messages = new();

    /// <summary>用户输入框文本</summary>
    [ObservableProperty]
    private string _inputText = string.Empty;

    /// <summary>是否正在等待 AI 响应</summary>
    [ObservableProperty]
    private bool _isWorking;

    /// <summary>是否显示 InfoBar 错误提示条</summary>
    [ObservableProperty]
    private bool _showInfoBar;

    /// <summary>InfoBar 错误消息文本</summary>
    [ObservableProperty]
    private string _infoBarMessage = string.Empty;

    /// <summary>InfoBar 严重级别</summary>
    [ObservableProperty]
    private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Error;

    /// <summary>Token 用量显示文本（底部状态栏）</summary>
    [ObservableProperty]
    private string _tokenDisplay = string.Empty;

    /// <summary>是否为离线模式</summary>
    [ObservableProperty]
    private bool _isOffline;

    /// <summary>Sidebar 是否展开</summary>
    [ObservableProperty]
    private bool _sidebarExpanded = true;

    /// <summary>待粘贴的图片字节数据</summary>
    [ObservableProperty]
    private byte[]? _pendingImageBytes;

    /// <summary>是否有待粘贴的图片</summary>
    public bool HasPendingImage => PendingImageBytes is not null;

    partial void OnPendingImageBytesChanged(byte[]? value)
        => OnPropertyChanged(nameof(HasPendingImage));

    public ChatViewModel(ChatAgentService chatAgent, IChatHistoryService historyService,
        IDangerousToolPolicy dangerPolicy, ModelRoutingService routing,
        TokenUsageService tokenUsage, ConversationSummarizer summarizer,
        PluginSharedState sharedState, ConversationStorageService convStorage,
        ConversationListViewModel conversationList,
        LocalCommandInterceptor localCmd,
        MessagePreprocessor preprocessor)
    {
        Log.Information("[ChatViewModel] 构造开始");
        _chatAgent = chatAgent;
        _historyService = historyService;
        _convStorage = convStorage;
        _routing = routing;
        _tokenUsage = tokenUsage;
        _summarizer = summarizer;
        _sharedState = sharedState;
        _localCmd = localCmd;
        _preprocessor = preprocessor;
        ConversationList = conversationList;

        // 异步网络探测
        _ = UpdateOfflineStatusAsync();

        // 设置高危工具确认回调（MAF 工具循环在后台线程，需封送到 UI 线程弹窗）
        dangerPolicy.DangerConfirmation = (toolName, argsSummary) =>
        {
            var title = toolName switch
            {
                "run_shell" => "执行命令",
                "write_file" => "写入文件",
                "delete_workflow" => "删除工作流",
                "delete_schedule" => "删除定时任务",
                _ => toolName
            };
            var message = $"AI 要执行以下操作：\n\n{title}\n{argsSummary}\n\n是否允许？";
            return Application.Current.Dispatcher.Invoke(() =>
                System.Windows.MessageBox.Show(message, "操作确认",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No)
                == System.Windows.MessageBoxResult.Yes);
        };

        // 订阅对话切换事件
        ConversationList.ConversationSwitched += OnConversationSwitchedAsync;

        // 从当前活跃对话加载历史
        var saved = _historyService.Load();
        if (saved.Count > 0)
        {
            foreach (var msg in saved)
                Messages.Add(msg);
        }

        // 检查是否有未发送的草稿（崩溃恢复）
        var draftPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalAssistant", "draft.txt");
        try
        {
            if (System.IO.File.Exists(draftPath))
            {
                var draft = System.IO.File.ReadAllText(draftPath);
                if (!string.IsNullOrWhiteSpace(draft))
                {
                    InputText = draft;
                    Messages.Add(new ChatMessage
                    {
                        Role = MessageRole.System,
                        Content = "[系统] 检测到上次未发送的消息，已恢复到输入框",
                        Timestamp = DateTime.Now
                    });
                }
                System.IO.File.Delete(draftPath);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[ChatViewModel] 草稿恢复失败"); }

        Log.Information("[ChatViewModel] 构造完成");
    }

    /// <summary>对话切换：保存当前 → 清空 → 加载新对话</summary>
    private async Task OnConversationSwitchedAsync(ConversationInfo conv)
    {
        // 保存当前对话
        _historyService.Save(Messages);

        // 清空 UI
        Messages.Clear();
        ShowInfoBar = false;

        // 重建 MAF Session
        await _chatAgent.SwitchConversationAsync();

        // 加载新对话消息
        var saved = _convStorage.LoadMessages(conv.Id);
        foreach (var msg in saved)
            Messages.Add(msg);

        // 更新草稿文件路径
        Log.Information("[ChatViewModel] 已切换到对话: {Id} ({Title})", conv.Id, conv.Title);
    }

    /// <summary>更新离线状态（异步网络探测）</summary>
    private async Task UpdateOfflineStatusAsync()
    {
        await _chatAgent.ProbeNetworkAsync();
        IsOffline = _chatAgent.IsOffline;
    }

    /// <summary>
    /// 发送用户消息到 AI 并流式更新回复。
    /// /clear 命令本地拦截处理（零 token）。
    /// 首次发送时携带磁盘历史以恢复 AI 上下文。
    /// </summary>
    [RelayCommand]
    private Task SendAsync() => SendInternalAsync(InputText?.Trim(), addUserMessage: true, clearInput: true);

    /// <summary>
    /// 核心发送逻辑。支持内部调用跳过添加用户消息（编辑/重新生成场景）。
    /// </summary>
    private async Task SendInternalAsync(string? text, bool addUserMessage, bool clearInput)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // 输入长度限制：超长截断并提示
        if (text.Length > MaxInputLength)
        {
            text = text[..MaxInputLength];
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = $"[系统] 输入过长，已自动截断至 {MaxInputLength} 字符",
                Timestamp = DateTime.Now
            });
        }

        // 记录输入历史
        if (_inputHistory.Count == 0 || _inputHistory[^1] != text)
        {
            if (_inputHistory.Count >= MaxInputHistory)
                _inputHistory.RemoveAt(0);
            _inputHistory.Add(text);
        }
        _historyIndex = _inputHistory.Count;

        // 崩溃恢复：先写入草稿文件，成功后再删除（仅普通发送时）
        var draftFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalAssistant", "draft.txt");
        if (addUserMessage)
        {
            try { System.IO.File.WriteAllText(draftFilePath, text); } catch { }
        }

        // 创建取消令牌
        _currentCts?.Cancel();
        _currentCts?.Dispose();
        _currentCts = new CancellationTokenSource();
        var ct = _currentCts.Token;

        if (clearInput)
            InputText = string.Empty;

        // /clear — 本地处理，零 token
        if (text.Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            _currentCts?.Dispose();
            _currentCts = null;
            await _chatAgent.ClearHistoryAsync();
            Messages.Clear();
            ShowInfoBar = false;
            _historyService.Save(Messages);
            return;
        }

        // ═══ 消息预处理管线：本地拦截 → 本地计算 → 主动插件 → 预搜索 ═══
        var preprocess = await _preprocessor.ProcessAsync(text);
        if (preprocess.HandledLocally)
        {
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.Assistant,
                Content = preprocess.LocalResponse!,
                Timestamp = DateTime.Now,
                ConversationId = _convStorage.ActiveConversationId
            });
            _currentCts?.Dispose();
            _currentCts = null;
            _historyService.Save(Messages);
            return;
        }
        var aiInputText = preprocess.AiInput; // 发给 AI 的文本（可能已被预处理增强）

        // 捕获当前待发的图片数据
        var imageBytes = PendingImageBytes;
        PendingImageBytes = null;

        // Add user message with optional image (skip for edit/regenerate)
        // 始终显示用户原始输入，不暴露系统注入的天气上下文
        if (addUserMessage)
        {
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.User,
                Content = text,
                Timestamp = DateTime.Now,
                ConversationId = _convStorage.ActiveConversationId,
                ImageBytes = imageBytes
            });
        }

        IsWorking = true;
        ShowInfoBar = false;

        // 每次发送前重新探测网络状态（而非依赖缓存值）
        await _chatAgent.ProbeNetworkAsync();
        IsOffline = _chatAgent.IsOffline;

        var assistantMsg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = "",
            Timestamp = DateTime.Now,
            ConversationId = _convStorage.ActiveConversationId
        };
        Messages.Add(assistantMsg);

        // ═══ 离线模式：强制走本地模型 ═══
        if (IsOffline)
        {
            try
            {
                var (localResponse, _) = await _routing.TryLocalAsync(aiInputText);
                assistantMsg.Content = localResponse;
                IsWorking = false;
                _tokenUsage.RecordUsage(aiInputText, localResponse, false);
                TokenDisplay = _tokenUsage.GetDisplayText();
                TrimDisplay();
                _historyService.Save(Messages);
                _currentCts.Dispose();
                _currentCts = null;
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ChatViewModel] 离线本地推理失败");
                assistantMsg.Content = "[离线模式] 本地模型暂不可用，请稍后重试。";
                IsWorking = false;
                _currentCts.Dispose();
                _currentCts = null;
                return;
            }
        }

        // ═══ 自动模型路由：语义意图分类 → 简单对话走本地（零 token） ═══
        if (_routing.ShouldTryLocal(aiInputText))
        {
            var intent = await _routing.ClassifyIntentAsync(aiInputText);
            Log.Debug("[ChatViewModel] 意图分类: {Intent} | {Msg}",
                intent, aiInputText.Length > 80 ? aiInputText[..80] + "..." : aiInputText);

            if (ModelRoutingService.IsLocalIntent(intent))
            {
                var (localResponse, isAdequate) = await _routing.TryLocalAsync(aiInputText);
                if (isAdequate)
                {
                    assistantMsg.Content = localResponse;
                    IsWorking = false;
                    _tokenUsage.RecordUsage(aiInputText, localResponse, false);
                    TokenDisplay = _tokenUsage.GetDisplayText();
                    TrimDisplay();
                    _historyService.Save(Messages);
                    _currentCts.Dispose();
                    _currentCts = null;
                    return;
                }
            }
        }

        // ═══ 需工具 / 本地不合格 → 远程模型 ═══
        _sharedState.CurrentRoundToolCalls.Clear();
        try
        {
            var fullContent = "";
            await foreach (var token in _chatAgent.SendMessageStreaming(aiInputText, imageBytes, ct))
            {
                fullContent += token;
                assistantMsg.Content = fullContent;
            }

            // 如果回复为空（纯工具调用场景），简要说明
            if (string.IsNullOrWhiteSpace(fullContent))
            {
                assistantMsg.Content = "[工具调用完成]";
            }

            // 记录本轮工具调用到消息上（供 UI 展示）
            foreach (var (toolName, result) in _sharedState.CurrentRoundToolCalls)
                assistantMsg.ToolCalls.Add($"{toolName}: {result}");

            // 记录远程 API 用量
            _tokenUsage.RecordUsage(aiInputText, fullContent, true);

            // 设置当前 Assistant 消息可重新生成
            assistantMsg.CanRegenerate = true;
            // 清除之前 Assistant 消息的可重新生成标记
            foreach (var m in Messages)
            {
                if (m != assistantMsg && m.Role == MessageRole.Assistant)
                    m.CanRegenerate = false;
            }
        }
        catch (OperationCanceledException)
        {
            assistantMsg.Content = string.IsNullOrWhiteSpace(assistantMsg.Content)
                ? "[已取消]" : assistantMsg.Content;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SendAsync 失败");
            var friendlyMsg = MapExceptionToMessage(ex);
            assistantMsg.Content = friendlyMsg;
            assistantMsg.IsError = true;

            InfoBarMessage = friendlyMsg;
            InfoBarSeverity = InfoBarSeverity.Error;
            ShowInfoBar = true;
        }
        finally
        {
            var cts = Interlocked.Exchange(ref _currentCts, null);
            cts?.Dispose();
            IsWorking = false;
            TokenDisplay = _tokenUsage.GetDisplayText();
            TrimDisplay();

            // 持久化到磁盘
            _historyService.Save(Messages);

            // 成功完成：删除草稿文件
            if (addUserMessage)
            {
                try
                {
                    if (System.IO.File.Exists(draftFilePath))
                        System.IO.File.Delete(draftFilePath);
                }
                catch (Exception ex) { Log.Debug(ex, "[ChatViewModel] 草稿清理失败"); }
            }

            // 递增摘要计数器并检查是否需要触发摘要
            _chatAgent.IncrementSummarizerRound();

            // 检测重复工具调用模式（由 ChatAgentService 内部的 PatternDetector 处理）
            // 通过 InfoBar 展示建议，不占用聊天消息列表
            var suggestion = _chatAgent.CollectPatternSuggestion();
            if (suggestion is not null)
            {
                InfoBarMessage = suggestion;
                InfoBarSeverity = InfoBarSeverity.Informational;
                ShowInfoBar = true;
            }

            // 触发对话摘要（本地模型，异步不阻塞）
            if (_chatAgent.ShouldSummarize)
            {
                _ = SummarizeAndPruneAsync();
            }
        }

        // 异步生成摘要并修剪旧消息（fire-and-forget）
        async Task SummarizeAndPruneAsync()
        {
            try
            {
                // 如果新一轮对话已开始，跳过本次摘要（避免与 SendAsync 竞态修改 Messages）
                if (IsWorking)
                    return;

                var summary = await _summarizer.SummarizeAsync(Messages);
                // 摘要生成期间可能已开始新一轮对话，再次检查
                if (summary is not null && !IsWorking)
                {
                    // 从显示列表中移除摘要过的旧消息（保留系统消息和最近 10 轮）
                    var keepCount = 20; // 10 rounds * 2 (user+assistant)
                    var toRemove = Messages
                        .Where(m => m.Role is MessageRole.User or MessageRole.Assistant)
                        .SkipLast(keepCount)
                        .ToList();

                    foreach (var msg in toRemove)
                        Messages.Remove(msg);

                    // 注入摘要提示
                    Messages.Insert(0, new ChatMessage
                    {
                        Role = MessageRole.System,
                        Content = $"[对话摘要] {summary}",
                        Timestamp = DateTime.Now
                    });

                    _historyService.Save(Messages);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[ChatViewModel] 摘要生成失败");
            }
        }
    }

    /// <summary>
    /// 清空消息列表和对话历史（绑定到 UI 按钮）
    /// </summary>
    [RelayCommand]
    private async Task Clear()
    {
        await _chatAgent.ClearHistoryAsync();
        Messages.Clear();
        ShowInfoBar = false;
        _historyService.Save(Messages);
    }

    /// <summary>
    /// 取消当前正在进行的 AI 流式响应
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        // 仅发送取消信号，不 Dispose（由 SendAsync finally 统一释放，避免 ObjectDisposedException）
        _currentCts?.Cancel();
    }

    /// <summary>切换侧边栏展开/折叠</summary>
    [RelayCommand]
    private void ToggleSidebar() => SidebarExpanded = !SidebarExpanded;

    // ──── 消息编辑 ────

    /// <summary>开始编辑用户消息</summary>
    [RelayCommand]
    private void StartEditMessage(ChatMessage? message)
    {
        if (message is null || message.Role != MessageRole.User || IsWorking) return;
        message.IsEditing = true;
        message.EditText = message.Content;
    }

    /// <summary>保存编辑并重新发送</summary>
    [RelayCommand]
    private async Task SaveEditMessage(ChatMessage? message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.EditText)) return;

        var editedText = message.EditText.Trim();
        message.Content = editedText;
        message.IsEditing = false;
        message.EditText = null;

        // 找到该消息在列表中的位置，移除其后所有消息
        var idx = Messages.IndexOf(message);
        if (idx < 0) return;

        // 移除编辑消息之后的所有消息
        while (Messages.Count > idx + 1)
            Messages.RemoveAt(Messages.Count - 1);

        // 清空 MAF 会话历史
        await _chatAgent.ClearHistoryAsync();

        // 重新发送编辑后的文本（不添加重复用户消息）
        _historyService.Save(Messages);
        await SendInternalAsync(editedText, addUserMessage: false, clearInput: false);
    }

    /// <summary>取消编辑</summary>
    [RelayCommand]
    private void CancelEditMessage(ChatMessage? message)
    {
        if (message is null) return;
        message.IsEditing = false;
        message.EditText = null;
    }

    // ──── 重新生成回复 ────

    /// <summary>重新生成最后一条 Assistant 回复</summary>
    [RelayCommand]
    private async Task RegenerateLastResponse()
    {
        if (IsWorking) return;

        // 找最后一条 Assistant 消息
        var lastAssistant = Messages.LastOrDefault(m => m.Role == MessageRole.Assistant);
        if (lastAssistant is null) return;

        // 找用户消息（Assistant 前面一条）
        var asstIdx = Messages.IndexOf(lastAssistant);
        if (asstIdx <= 0) return;

        var userMsg = Messages[asstIdx - 1];
        if (userMsg.Role != MessageRole.User) return;

        // 移除 Assistant 消息
        Messages.RemoveAt(asstIdx);

        // 清空 MAF Session
        await _chatAgent.ClearHistoryAsync();

        _historyService.Save(Messages);

        // 重新发送（不添加重复用户消息）
        await SendInternalAsync(userMsg.Content, addUserMessage: false, clearInput: false);
    }

    // ──── 图片输入 ────

    /// <summary>从剪贴板粘贴图片（Ctrl+V 或按钮）</summary>
    [RelayCommand]
    private void PasteImage()
    {
        if (IsWorking) return;

        if (!System.Windows.Clipboard.ContainsImage())
        {
            // 非图片剪贴板：正常处理文本粘贴（由 ChatView 处理）
            return;
        }

        try
        {
            var bitmap = System.Windows.Clipboard.GetImage();
            if (bitmap is null) return;

            // 缩放大图（限制最大尺寸 1024x1024）
            var maxDim = 1024;
            if (bitmap.PixelWidth > maxDim || bitmap.PixelHeight > maxDim)
            {
                var scale = Math.Min((double)maxDim / bitmap.PixelWidth, (double)maxDim / bitmap.PixelHeight);
                var w = (int)(bitmap.PixelWidth * scale);
                var h = (int)(bitmap.PixelHeight * scale);
                bitmap = new System.Windows.Media.Imaging.TransformedBitmap(
                    bitmap, new System.Windows.Media.ScaleTransform(scale, scale));
            }

            // 编码为 PNG 字节
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            var bytes = ms.ToArray();

            // 大小限制 20MB
            if (bytes.Length > 20 * 1024 * 1024)
            {
                Messages.Add(new ChatMessage
                {
                    Role = MessageRole.System,
                    Content = "[系统] 图片过大（超过 20MB），请缩小后重试",
                    Timestamp = DateTime.Now
                });
                return;
            }

            PendingImageBytes = bytes;
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = "[系统] 图片已粘贴，输入文本后发送",
                Timestamp = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ChatViewModel] 粘贴图片失败");
        }
    }

    /// <summary>清除待发送的图片</summary>
    [RelayCommand]
    private void ClearPendingImage()
    {
        PendingImageBytes = null;
    }

    // ──── 文件拖拽 ────

    /// <summary>处理拖放的文件</summary>
    public void HandleDroppedFiles(string[] files)
    {
        if (IsWorking || files.Length == 0) return;

        List<string> parts = new();
        foreach (var file in files)
        {
            var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp")
            {
                // 图片文件：读取字节并设置为待发图片
                try
                {
                    var bytes = System.IO.File.ReadAllBytes(file);
                    if (bytes.Length > 20 * 1024 * 1024)
                    {
                        parts.Add($"[图片过大跳过: {System.IO.Path.GetFileName(file)}]");
                        continue;
                    }
                    PendingImageBytes = bytes;
                    parts.Add($"[图片: {System.IO.Path.GetFileName(file)}]");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[ChatViewModel] 读取拖放图片失败: {Path}", file);
                }
            }
            else if (ext is ".txt" or ".cs" or ".json" or ".xml" or ".md" or ".log" or ".py" or ".js" or ".ts")
            {
                // 文本文件：读取前 10KB 填入输入框
                try
                {
                    var content = System.IO.File.ReadAllText(file);
                    if (content.Length > 10 * 1024)
                        content = content[..(10 * 1024)] + "\n...[文件过长已截断]";
                    parts.Add($"\n--- 文件: {System.IO.Path.GetFileName(file)} ---\n{content}");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[ChatViewModel] 读取拖放文件失败: {Path}", file);
                }
            }
            else if (ext is ".pdf" or ".docx" or ".doc" or ".xlsx" or ".xls")
            {
                // Office/PDF 文件：预填 read_file 指令
                parts.Add($"\n[拖入文件: {System.IO.Path.GetFileName(file)}]\n");
                InputText += $"请读取并分析这个文件: {file}";
            }
            else
            {
                // 其他文件：填路径
                parts.Add($"\n[文件: {System.IO.Path.GetFileName(file)}]");
                if (string.IsNullOrWhiteSpace(InputText))
                    InputText += $"我拖入了这个文件: {file}";
            }
        }

        if (parts.Count > 0)
        {
            var text = string.Join("\n", parts);
            if (!string.IsNullOrWhiteSpace(InputText))
                InputText += text;
            else if (parts.Any(p => p.StartsWith("[图片:")))
                InputText += "请描述这张图片的内容";
            else
                InputText = text;
        }
    }

    /// <summary>
    /// 向上/向下箭头导航输入历史。
    /// 返回应填入输入框的历史文本，null 表示无历史。
    /// </summary>
    /// <param name="direction">-1=上一条, 1=下一条</param>
    public string? NavigateInputHistory(int direction)
    {
        if (_inputHistory.Count == 0)
            return null;

        _historyIndex += direction;

        // 到顶 → 回到第一条
        if (_historyIndex < 0)
        {
            _historyIndex = -1;
            return "";  // 返回空字符串表示清空输入框
        }

        // 超出最新 → 清空
        if (_historyIndex >= _inputHistory.Count)
        {
            _historyIndex = _inputHistory.Count;
            return "";
        }

        return _inputHistory[_historyIndex];
    }

    /// <summary>将异常映射为用户友好的中文提示</summary>
    private static string MapExceptionToMessage(Exception ex)
    {
        var msg = ex.Message;
        return ex switch
        {
            HttpRequestException => "网络连接失败，请检查网络后重试",
            TimeoutException => "请求超时，服务器响应过慢，请稍后重试",
            TaskCanceledException => "请求超时，请稍后重试",
            OperationCanceledException => "操作已取消",
            _ when msg.Contains("401") || msg.Contains("Unauthorized") || msg.Contains("unauthorized")
                => "API 密钥无效，请在设置中检查 API Key 是否正确",
            _ when msg.Contains("429") || msg.Contains("rate")
                => "请求过于频繁，请稍等片刻再试",
            _ when msg.Contains("503") || msg.Contains("502")
                => "AI 服务暂时不可用，请稍后重试",
            _ when msg.Contains("402") || msg.Contains("quota") || msg.Contains("insufficient")
                => "API 额度不足，请检查账户余额",
            _ when msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                => "请求超时，请稍后重试",
            _ => $"出错了: {msg}"
        };
    }

    private void TrimDisplay()
    {
        while (Messages.Count > MaxDisplayMessages)
            Messages.RemoveAt(0);
    }
}
