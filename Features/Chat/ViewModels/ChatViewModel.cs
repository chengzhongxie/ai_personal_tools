using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAssistant.Features.Chat.Models;
using PersonalAssistant.Features.Chat.Models.Enums;
using PersonalAssistant.Features.Chat.Services;
using PersonalAssistant.Features.Scheduler.Models;
using PersonalAssistant.Features.Scheduler.Services;
using PersonalAssistant.Features.Workflow.Models;
using PersonalAssistant.Features.Workflow.Services;
using Serilog;
using Wpf.Ui.Controls;

namespace PersonalAssistant.Features.Chat.ViewModels;

/// <summary>
/// 聊天界面的 ViewModel，管理消息列表、流式 AI 响应、斜杠命令和模式建议
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private const int MaxDisplayMessages = 200;

    private readonly ChatAgentService _chatAgent;
    private readonly WorkflowRecorder _recorder;
    private readonly PatternDetector _patternDetector;
    private readonly WorkflowStorageService _workflowStorage;
    private readonly WorkflowExecutorService _workflowExecutor;
    private readonly SchedulerStorageService _schedulerStorage;

    /// <summary>待确认的模式建议（用户回复 yes 名称 时保存）</summary>
    private PatternMatch? _pendingSuggestion;

    /// <summary>是否正在录制教学模式</summary>
    private bool _isRecording;
    /// <summary>当前录制的工作流名称</summary>
    private string _recordingName = "";
    /// <summary>录制过程中累积的所有步骤</summary>
    private List<ToolCallRecord> _recordedSteps = new();

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

    public ChatViewModel(
        ChatAgentService chatAgent,
        WorkflowRecorder recorder,
        PatternDetector patternDetector,
        WorkflowStorageService workflowStorage,
        WorkflowExecutorService workflowExecutor,
        SchedulerStorageService schedulerStorage)
    {
        _chatAgent = chatAgent;
        _recorder = recorder;
        _patternDetector = patternDetector;
        _workflowStorage = workflowStorage;
        _workflowExecutor = workflowExecutor;
        _schedulerStorage = schedulerStorage;
    }

    /// <summary>
    /// 发送用户消息到 AI 并流式更新回复
    /// </summary>
    [RelayCommand]
    private async Task SendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        InputText = string.Empty;

        // Handle slash commands
        if (await TryHandleCommand(text))
            return;

        // Add user message
        Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Content = text,
            Timestamp = DateTime.Now
        });

        IsWorking = true;
        ShowInfoBar = false;

        // Placeholder for streaming response
        var assistantMsg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = "",
            Timestamp = DateTime.Now
        };
        Messages.Add(assistantMsg);

        try
        {
            var fullContent = "";
            await foreach (var token in _chatAgent.SendMessageStreaming(text))
            {
                fullContent += token;
                assistantMsg.Content = fullContent;
            }

            // 如果回复为空（纯工具调用场景），填充占位
            if (string.IsNullOrWhiteSpace(fullContent))
            {
                assistantMsg.Content = "[工具调用完成]";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SendAsync 失败");
            assistantMsg.Content = $"未知错误: {ex.Message}";
            assistantMsg.IsError = true;

            InfoBarMessage = ex.Message;
            InfoBarSeverity = InfoBarSeverity.Error;
            ShowInfoBar = true;
        }
        finally
        {
            IsWorking = false;
            TrimDisplay();

            if (_isRecording)
            {
                // 教学模式：收集本轮工具调用，追加到累积列表
                var roundSteps = _recorder.CollectRoundRecords();
                _recordedSteps.AddRange(roundSteps);
                if (roundSteps.Count > 0)
                {
                    Messages.Add(new ChatMessage
                    {
                        Role = MessageRole.Tool,
                        Content = $"已录制 {roundSteps.Count} 个步骤: {string.Join(" → ", roundSteps.Select(s => s.ToolName))}",
                        Timestamp = DateTime.Now
                    });
                }
            }
            else
            {
                // 正常模式：检测重复模式
                DetectPatterns();
            }
        }
    }

    /// <summary>
    /// 收集本轮工具序列并检测重复模式
    /// </summary>
    private void DetectPatterns()
    {
        var sequence = _recorder.CollectRound();
        if (sequence.Count == 0) return;

        var pattern = _patternDetector.AddRound(sequence);
        if (pattern is not null)
        {
            _pendingSuggestion = pattern;

            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = $"检测到重复操作模式：{string.Join(" → ", pattern.ToolSequence)} " +
                          $"(已出现 {pattern.OccurrenceCount} 次)。\n" +
                          $"要保存为工作流吗？回复 \"yes {pattern.SuggestedName}\" 保存，或回复其他内容忽略。",
                Timestamp = DateTime.Now
            });
        }
    }

    /// <summary>处理斜杠命令，返回 true 表示已处理</summary>
    private async Task<bool> TryHandleCommand(string text)
    {
        // /record <name> - 开始教学模式，录制后续所有工具调用
        if (text.StartsWith("/record ", StringComparison.OrdinalIgnoreCase))
        {
            var name = text[8..].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                Messages.Add(new ChatMessage { Role = MessageRole.System, Content = "用法: /record <工作流名称>", Timestamp = DateTime.Now });
                return true;
            }
            _isRecording = true;
            _recordingName = name;
            _recordedSteps = new List<ToolCallRecord>();
            _recorder.CollectRound(); // 清空之前的录制
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = $"教学模式已开启: \"{name}\"。现在一步步告诉我怎么操作，我会记录下来。完成后输入 /stop。",
                Timestamp = DateTime.Now
            });
            return true;
        }

        // /stop - 停止教学模式并保存工作流
        if (text.Equals("/stop", StringComparison.OrdinalIgnoreCase))
        {
            if (!_isRecording)
            {
                Messages.Add(new ChatMessage { Role = MessageRole.System, Content = "当前没有正在录制的教学模式。使用 /record <名称> 开始。", Timestamp = DateTime.Now });
                return true;
            }
            _isRecording = false;

            if (_recordedSteps.Count == 0)
            {
                Messages.Add(new ChatMessage { Role = MessageRole.System, Content = "没有录制到任何操作步骤，工作流未保存。", Timestamp = DateTime.Now, IsError = true });
                return true;
            }

            var wf = new WorkflowDefinition
            {
                Name = _recordingName,
                Description = $"手动录制的操作序列 ({_recordedSteps.Count} 个步骤): {string.Join(" → ", _recordedSteps.Select(s => s.ToolName))}",
                Steps = _recordedSteps,
                CreatedAt = DateTime.Now
            };
            _workflowStorage.Save(wf);

            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = $"工作流 \"{_recordingName}\" 已保存 ({_recordedSteps.Count} 个步骤)。\n" +
                          $"步骤: {string.Join(" → ", _recordedSteps.Select(s => $"{s.ToolName}({s.Args})"))}\n" +
                          $"使用 /run {_recordingName} 执行，/workflows 查看所有。",
                Timestamp = DateTime.Now
            });
            _recordedSteps = new();
            return true;
        }

        // /clear
        if (text.Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            await _chatAgent.ClearHistoryAsync();
            _patternDetector.Reset();
            _recorder.CollectRound(); // 清空未收集的录制
            Messages.Clear();
            ShowInfoBar = false;
            _pendingSuggestion = null;
            return true;
        }

        // 建议确认：yes <name>
        if (text.StartsWith("yes ", StringComparison.OrdinalIgnoreCase))
        {
            var name = text[4..].Trim();
            if (_pendingSuggestion is not null && !string.IsNullOrWhiteSpace(name))
            {
                SaveWorkflowFromSuggestion(name);
                _pendingSuggestion = null;
                return true;
            }
            return false; // 没有待确认的建议，当作普通消息处理
        }

        // /workflows - 列出已保存的工作流
        if (text.Equals("/workflows", StringComparison.OrdinalIgnoreCase))
        {
            var names = _workflowStorage.ListAll();
            if (names.Count == 0)
            {
                Messages.Add(new ChatMessage
                {
                    Role = MessageRole.System,
                    Content = "没有已保存的工作流。重复相同操作 3 次后，系统会自动建议保存。",
                    Timestamp = DateTime.Now
                });
            }
            else
            {
                Messages.Add(new ChatMessage
                {
                    Role = MessageRole.System,
                    Content = "已保存的工作流:\n" + string.Join("\n", names.Select(n => $"  - {n}")),
                    Timestamp = DateTime.Now
                });
            }
            return true;
        }

        // /run <name> - 本地回放工作流
        if (text.StartsWith("/run ", StringComparison.OrdinalIgnoreCase))
        {
            var name = text[5..].Trim();
            var wf = _workflowStorage.Load(name);
            if (wf is null)
            {
                Messages.Add(new ChatMessage
                {
                    Role = MessageRole.System,
                    Content = $"工作流 \"{name}\" 未找到。使用 /workflows 查看已保存列表。",
                    Timestamp = DateTime.Now,
                    IsError = true
                });
            }
            else
            {
                Messages.Add(new ChatMessage
                {
                    Role = MessageRole.System,
                    Content = $"正在执行工作流: {name} ({wf.Steps.Count} 个步骤)...",
                    Timestamp = DateTime.Now
                });

                try
                {
                    var result = await _workflowExecutor.ExecuteAsync(wf);
                    Messages.Add(new ChatMessage
                    {
                        Role = MessageRole.Assistant,
                        Content = result,
                        Timestamp = DateTime.Now
                    });
                }
                catch (Exception ex)
                {
                    Messages.Add(new ChatMessage
                    {
                        Role = MessageRole.System,
                        Content = $"工作流执行失败: {ex.Message}",
                        Timestamp = DateTime.Now,
                        IsError = true
                    });
                }
            }
            return true;
        }

        // /delete <name> - 删除工作流
        if (text.StartsWith("/delete ", StringComparison.OrdinalIgnoreCase))
        {
            var name = text[8..].Trim();
            var deleted = _workflowStorage.Delete(name);
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = deleted
                    ? $"工作流 \"{name}\" 已删除。"
                    : $"工作流 \"{name}\" 未找到。",
                Timestamp = DateTime.Now
            });
            return true;
        }

        // /schedule add "HH:mm" <toolName> "args"
        if (text.StartsWith("/schedule add ", StringComparison.OrdinalIgnoreCase))
        {
            HandleScheduleAdd(text[14..].Trim());
            return true;
        }

        // /schedules — 列出所有定时任务
        if (text.Equals("/schedules", StringComparison.OrdinalIgnoreCase))
        {
            HandleSchedulesList();
            return true;
        }

        // /schedule delete "name"
        if (text.StartsWith("/schedule delete ", StringComparison.OrdinalIgnoreCase))
        {
            HandleScheduleDelete(text[17..].Trim());
            return true;
        }

        return false;
    }

    /// <summary>解析 /schedule add "HH:mm" toolName "args" 命令</summary>
    private void HandleScheduleAdd(string args)
    {
        // 解析格式: "HH:mm" toolName "args"
        var match = Regex.Match(args,
            @"^""(\d{2}:\d{2})""\s+(\S+)(?:\s+""(.+)""|\s+(.+))?$");

        if (!match.Success)
        {
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = "用法: /schedule add \"HH:mm\" <工具名> \"参数\"\n" +
                          "例如: /schedule add \"09:00\" run_shell \"echo hello\"\n" +
                          "      /schedule add \"08:30\" run_command Chrome\n" +
                          "已知工具: " + string.Join(", ", SchedulerService.KnownTools.OrderBy(t => t)),
                Timestamp = DateTime.Now
            });
            return;
        }

        var timeOfDay = match.Groups[1].Value;
        var toolName = match.Groups[2].Value;
        var toolArgs = match.Groups[3].Success
            ? match.Groups[3].Value
            : match.Groups[4].Success
                ? match.Groups[4].Value
                : string.Empty;

        // 校验时间格式
        if (!TimeSpan.TryParse(timeOfDay, out var ts) || ts.TotalMinutes < 0 || ts.TotalDays >= 1)
        {
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = $"无效的时间格式: \"{timeOfDay}\"。请使用 HH:mm 格式（如 09:00）。",
                Timestamp = DateTime.Now,
                IsError = true
            });
            return;
        }

        // 校验工具名
        if (!SchedulerService.KnownTools.Contains(toolName))
        {
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = $"未知工具: \"{toolName}\"。\n已知工具: {string.Join(", ", SchedulerService.KnownTools.OrderBy(t => t))}",
                Timestamp = DateTime.Now,
                IsError = true
            });
            return;
        }

        // 使用 HH:mm 格式作为名称
        var taskName = $"{toolName}_{timeOfDay.Replace(":", "")}";

        var task = new ScheduledTask
        {
            Name = taskName,
            TimeOfDay = timeOfDay,
            ToolName = toolName,
            ToolArgs = toolArgs,
            IsEnabled = true,
            CreatedAt = DateTime.Now
        };
        _schedulerStorage.Save(task);

        Messages.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Content = $"定时任务已创建:\n" +
                      $"  名称: {taskName}\n" +
                      $"  时间: 每天 {timeOfDay}\n" +
                      $"  操作: {toolName} {toolArgs}\n" +
                      $"使用 /schedules 查看所有任务。",
            Timestamp = DateTime.Now
        });
    }

    /// <summary>处理 /schedules 命令 — 列出所有定时任务</summary>
    private void HandleSchedulesList()
    {
        var names = _schedulerStorage.ListAll();
        if (names.Count == 0)
        {
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = "没有已保存的定时任务。\n" +
                          "使用 /schedule add \"HH:mm\" <工具名> \"参数\" 创建。",
                Timestamp = DateTime.Now
            });
        }
        else
        {
            var lines = new List<string> { "已保存的定时任务:" };
            foreach (var name in names)
            {
                var task = _schedulerStorage.Load(name);
                if (task is null) continue;

                var status = task.IsEnabled ? "启用" : "禁用";
                var lastRun = task.LastRunDate is not null
                    ? $"上次运行: {task.LastRunDate}"
                    : "从未运行";
                lines.Add($"  [{status}] {task.Name} — 每天 {task.TimeOfDay} → {task.ToolName} {task.ToolArgs} ({lastRun})");
            }
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = string.Join("\n", lines),
                Timestamp = DateTime.Now
            });
        }
    }

    /// <summary>处理 /schedule delete "name" 命令</summary>
    private void HandleScheduleDelete(string name)
    {
        // 去除可能的外层引号
        name = name.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(name))
        {
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = "用法: /schedule delete \"任务名称\"",
                Timestamp = DateTime.Now
            });
            return;
        }

        var deleted = _schedulerStorage.Delete(name);
        Messages.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Content = deleted
                ? $"定时任务 \"{name}\" 已删除。"
                : $"定时任务 \"{name}\" 未找到。使用 /schedules 查看已保存列表。",
            Timestamp = DateTime.Now
        });
    }

    /// <summary>从检测到的模式保存工作流</summary>
    private void SaveWorkflowFromSuggestion(string name)
    {
        var records = WorkflowRecorder.SequenceToRecords(_pendingSuggestion!.ToolSequence);
        var wf = new WorkflowDefinition
        {
            Name = name,
            Description = $"自动检测的重复模式: {string.Join(" → ", _pendingSuggestion.ToolSequence)} " +
                          $"(出现 {_pendingSuggestion.OccurrenceCount} 次)",
            Steps = records,
            CreatedAt = DateTime.Now
        };
        _workflowStorage.Save(wf);

        Messages.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Content = $"工作流 \"{name}\" 已保存。使用 /workflows 查看，/run {name} 执行。",
            Timestamp = DateTime.Now
        });
    }

    /// <summary>
    /// 清空消息列表和对话历史
    /// </summary>
    [RelayCommand]
    private async Task Clear()
    {
        await _chatAgent.ClearHistoryAsync();
        _patternDetector.Reset();
        _recorder.CollectRound();
        Messages.Clear();
        ShowInfoBar = false;
        _pendingSuggestion = null;
    }

    private void TrimDisplay()
    {
        while (Messages.Count > MaxDisplayMessages)
            Messages.RemoveAt(0);
    }
}
