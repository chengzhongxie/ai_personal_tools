# PersonalAssistant - Claude 项目规范

> WPF 桌面 AI 助手，通过 DeepSeek API 提供 AI 对话+工具调用能力。
> 带卡通机器人悬浮窗 + 系统托盘，支持开机自启动。
> 基于 Microsoft Agent Framework (MAF) 实现 AI 对话、工具调用循环和流式输出。
> 插件化架构：工具方法自包含在 Plugins/ 目录，通过 IToolPlugin 接口热插拔。

## 项目核心目的与价值

> **最大化利用 Windows 系统资源，实现 AI 驱动的桌面自动化，同时通过本地模型路由和代码逻辑将 Token 消耗降至最低。**

| 核心价值 | 说明 |
|----------|------|
| **Windows 深度集成** | 不追求跨平台，聚焦 Windows 原生能力（Win32 API、窗口操控、进程管理、剪贴板、热键、开机自启），将系统资源利用到极致 |
| **AI 自动化（能动手，不只是聊天）** | AI 可直接操作系统：读写文件、执行 Shell、操控窗口、发送按键、启动程序、搜索文档——是 "AI Agent" 而非 "AI Chatbot" |
| **Token 消耗最小化** | 3 层模型路由（简单→本地零 token、复杂→远程）、工作流本地回放、代码逻辑优先（能用代码就不用 AI），将 API 成本压到最低 |
| **离线可用** | 本地 LLM（Qwen 0.5B）保证断网时基础功能不中断，数据不出本机 |
| **可扩展** | 单文件 .cs 插件 + GitHub Gists 社区市场，用户可按需扩展 AI 能力 |

**设计决策原则：** 任何功能的设计与实现，优先服务于以上核心价值。不因跨平台兼容性牺牲 Windows 深度能力，不因追逐新技术而增加 Token 消耗。

## 维护规则（强制）

**对项目的任何代码改动（新增/删除文件、修改功能、变更架构、调整配置），必须在提交前同步更新本 CLAUDE.md 文件中对应的章节。** 模板文件 `docs/template/CLAUDE_TEMPLATE.md` 也需要同步更新。

## 模板引用

本项目遵循 `docs/template/CLAUDE_TEMPLATE.md` 中的通用规范（架构、MVVM、DI、日志、安全护栏等）。
以下仅描述本项目的特有内容。

## Token 消耗最小化原则（强制）

> 每新增功能，优先以**本地程序逻辑**实现，处理不了的才请求 AI 模型，减少 token 消耗。

| 规则 | 说明 |
|------|------|
| **斜杠命令统一为 AI 工具** | 工作流/定时任务管理已统一为 AI 工具层（list_workflows, delete_schedule 等），用户用自然语言操作。仅 `/clear` 保留本地拦截（零 token，高频操作） |
| **定时任务免 AI** | SchedulerService 直接调用 `IToolPluginHost.ExecuteToolStepAsync()` 本地路由执行，零 token 消耗 |
| **工作流回放免 AI** | WorkflowExecutorService 本地遍历步骤序列，通过 IToolPluginHost 直接执行工具方法，不经过 AI |
| **新增命令先评估** | 新增功能时优先考虑：AI 工具方法（需要理解/生成自然语言时）vs 本地代码逻辑（纯逻辑操作时） |
| **仅自然语言对话调 AI** | 只有用户输入的普通聊天消息（非斜杠命令）才发送给 DeepSeek API |

## 代码优先原则（强制）

> 能通过代码逻辑实现的功能，优先写代码，不依赖 AI 模型。AI 仅用于需要理解/生成自然语言的场景。

| 场景 | 实现方式 |
|------|------|
| 定时任务调度 | 代码逻辑：`System.Threading.Timer` + `IToolPluginHost` 路由 |
| 工作流录制/回放 | 代码逻辑：`List<ToolCallRecord>` + 本地遍历执行 |
| 模式检测 | 代码逻辑：环形缓冲 + 序列匹配算法 |
| /clear 命令解析 | 代码逻辑：`string.Equals` 本地拦截 |
| 工具执行 | 代码逻辑：`PluginAggregator.ExecuteToolStepAsync()` 遍历插件 |
| 文件读写/进程启动 | 代码逻辑：`System.IO` / `Process.Start` |
| 自然语言对话 | AI 模型：用户发起的普通聊天消息 |

**新增功能决策流程：**
1. 需求能用代码逻辑实现 → 写代码
2. 需求需要理解/生成/推理能力 → 调 AI

---

## 项目特有技术栈

| 用途 | 库/技术 |
|------|---------|
| AI 框架 | Microsoft Agent Framework (MAF) via `Microsoft.Agents.AI.OpenAI` 1.10.0 |
| AI 底层 | DeepSeek API (via OpenAI .NET SDK 2.11.0) |
| 聊天界面 | WPF + 深色/浅色可切换主题 Chat Bubble UI |
| MVVM | CommunityToolkit.Mvvm 8.4.0 |
| DI | Scrutor 6.1.0 自动扫描 |
| UI 框架 | WPF-UI 4.0.3 (FluentWindow, InfoBar, ProgressRing) |
| 日志 | Serilog 4.3.0 |
| 本地 LLM | LLamaSharp 0.27.0 + Qwen2.5-0.5B-Instruct GGUF (离线，零 token) |
| 主题 | DynamicResource 主题系统（深色/浅色可切换） |
| 用户配置 | `%APPDATA%\PersonalAssistant\settings.json` (不入库，每台电脑独立) |

## 架构：插件化工具系统

```
ChatAgentService (MAF 生命周期 + 流式输出, ~260行)
    │
    ├── IToolPluginHost (PluginAggregator)  ← WorkflowExecutorService
    │       │                                   ← SchedulerService
    │       ├── GetAllTools() → AIFunction[]
    │       ├── ExecuteToolStepAsync() → 遍历插件
    │       └── GetAggregatedPrompt()
    │
    └── IDangerousToolPolicy (PluginAggregator)
            └── DangerConfirmation + ConfirmDangerous()

PluginAggregator 自动发现所有 IToolPlugin（DI IEnumerable<IToolPlugin> + PluginLoader）:
    │  外部插件优先
    ├── [External] PluginLoader → PluginBase → ExternalPluginAdapter → IToolPlugin
    ├── SystemToolsPlugin       (13 tools: read_file, write_file, ...)
    ├── WebToolsPlugin          (2 tools:  web_fetch, web_search)
    ├── SystemInfoPlugin        (2 tools:  system_info, screenshot)
    ├── ChatToolsPlugin         (2 tools:  clear_chat, notify)
    ├── SchedulerPlugin         (3 tools:  add/list/delete_schedule)
    ├── WorkflowPlugin          (4 tools:  list/run/delete/save_workflow)
    ├── LocalLLMPlugin          (1 tool:   local_llm)
    └── KnowledgeBasePlugin     (1 tool:   knowledge_search)
```

### 三个核心接口（`Core/Interfaces/`）

| 接口 | 职责 |
|------|------|
| `IToolPlugin` | 插件契约：提供 AIFunction[] + TryExecuteToolAsync + 提示词片段 |
| `IToolPluginHost` | 聚合器接口：GetAllTools() + ExecuteToolStepAsync() + GetAggregatedPrompt() |
| `IDangerousToolPolicy` | 危险操作确认策略：DangerConfirmation + ConfirmDangerous() |

### 循环依赖解决方案

```
旧：ChatAgentService → WorkflowExecutorService → ChatAgentService (循环!)
新：ChatAgentService → IToolPluginHost ← WorkflowExecutorService (DAG ✓)
                        ↑
              PluginAggregator (实现 IToolPluginHost)
```

所有三个消费者（ChatAgentService、WorkflowExecutorService、SchedulerService）都依赖 `IToolPluginHost` 接口，不再互相依赖。

### 添加新插件（零 DI 配置）

创建 2 个文件即可，无需触碰任何现有代码：
```
Features/Plugins/DocumentTools/
├── DocumentPlugin.cs        # IToolPlugin 实现
└── DocumentToolMethods.cs   # 工具方法实现，含 [Description] 属性
```

插件通过 DI 自动发现（`IToolPlugin` 扫描），工具自动注册到 AI Agent。

### 外部插件系统

> 单文件 `.cs` 开发 → 放入 `%APPDATA%\PersonalAssistant\Plugins\` 目录 → 重启即用，无需重新编译主项目。
> 完整开发文档：`docs/PLUGIN_DEV_GUIDE.md`

**三层架构：**

| 层 | 组件 | 说明 |
|----|------|------|
| 底座 | `PluginBase` (Core/Plugins/) | 插件作者唯一需继承的抽象类。重写 4 个成员：Name, Description, GetToolDefinitions(), ExecuteToolAsync() |
| 加载 | `PluginLoader` (Core/Plugins/) | Roslyn 编译 .cs → 反射发现 PluginBase 子类 → 实例化。编译失败跳过 |
| 桥接 | `ExternalPluginAdapter` (Core/Plugins/) | 包装 PluginBase 为 IToolPlugin，运行时用 AIFunctionFactory.Create 生成 AIFunction[] |

**优先级规则：**
- 外部插件优先于内置插件（PluginAggregator 将外部插件放在 _plugins 列表最前面）
- 外部插件工具名与内置冲突时 → warning 日志 → 外部版本生效（可覆盖内置工具）
- 编译失败的插件 → warning 日志 → 跳过该文件，其他插件正常运行

**PluginBase API（零外部依赖）：**

```csharp
public abstract class PluginBase
{
    public abstract string Name { get; }                    // 插件名
    public abstract string Description { get; }             // 插件描述
    public abstract PluginToolDefinition[] GetToolDefinitions(); // 工具元数据
    public abstract Task<string?> ExecuteToolAsync(string toolName, string args); // 执行工具
    public virtual string? GetPromptFragment() => null;     // 可选提示词片段
    public string? SourceFilePath { get; set; }             // 外部插件源文件路径（由 PluginLoader 设置）
}
```

**PluginToolDefinition DTO：**
```csharp
public sealed class PluginToolDefinition
{
    public string Name { get; init; }              // 工具名
    public string Description { get; init; }       // AI 模型看到的描述
    public IReadOnlyList<PluginParameter>? Parameters { get; init; } // null = 无参数
}
```

**资源成本：** 启动时一次性 ~50-200ms CPU（Roslyn 编译，AssemblyLoadContext 隔离加载），用完 GC。之后零开销。支持 collectible 卸载（热重载就绪）。

## 功能模块

### Chat
- **ChatAgentService**：~300 行，仅负责 MAF AIAgent 生命周期管理 + 流式输出 + 模式建议收集。工具方法全部移到 Plugins/，通过 `IToolPluginHost.GetAllTools()` 获取 AIFunction 数组，通过 `IToolPluginHost.GetAggregatedPrompt()` 获取聚合提示词。支持 `SendMessageStreaming(message, imageBytes?, ct)` 流式输出（含可选图片附件，通过 MEAI DataContent 传递多模态消息）和 `ClearHistoryAsync()` 清空历史+重置模式检测（异步事件处理，旧 Session 延迟 Dispose），以及 `SwitchConversationAsync()` 切换对话时重建 MAF Session。内建 PatternDetector 模式检测（≥4 次重复 + ≥3 工具序列），通过 `CollectPatternSuggestion()` 返回建议。离线探测（`ProbeNetworkAsync()`，3s 超时 HEAD 请求）+ `IsOffline` 属性。对话摘要轮次计数（`IncrementSummarizerRound()` / `ShouldSummarize`）+ 摘要提示词片段注入。`SemaphoreSlim(1,1)` 防止 `SendMessageStreaming` 和 `ClearHistoryAsync` 并发执行，`OnClearChat` 事件通过 async void 等待锁释放后执行。资源成本：仅消息发送时消耗，空闲时零开销（事件驱动）。
- **ChatSystemPrompt**：系统提示词构建器。基础提示词 + 自定义系统提示词（来自 UserSettings，优先于默认） + 聚合所有插件的提示词片段 + 对话摘要片段。线程安全锁保护缓存（双重检查锁定），cache键包含 pluginFragments + customPrompt，不变则复用（零分配）。
- **ChatViewModel**：消息列表管理、流式 AI 响应更新、`/clear` 本地拦截、数学/日期本地计算、对话持久化（自动保存/恢复）、模式建议展示（通过 InfoBar 提示，不占用消息列表）、InfoBar 错误显示、取消流式响应（CancelCommand）、输入历史记录（Up/Down 箭头导航，最近 50 条）。消息上限 200 条自动修剪。多对话管理（引用 ConversationListViewModel，对话切换时保存/加载/清空 Messages）。消息编辑（StartEditMessage/SaveEditMessage/CancelEditMessage 命令：编辑后修剪后续消息 → 重建 Session → 重新发送）。重新生成回复（RegenerateLastResponse 命令：移除最后 Assistant → 复用编辑路径重发）。图片粘贴（PasteImageCommand：Clipboard.ContainsImage() → 缩放大图 → PendingImageBytes）。文件拖拽处理（HandleDroppedFiles：文本文件读 10KB 粘贴，PDF/Office 预填指令，图片路由到粘贴）。内建自动模型路由（ModelRoutingService），简单对话走本地模型（零 token），复杂/需工具走远程。离线模式：网络不可用时强制走本地模型。Token 用量显示（底部状态栏 `TokenDisplay`）。对话摘要集成。依赖 ChatAgentService + IChatHistoryService + IDangerousToolPolicy + ModelRoutingService + TokenUsageService + ConversationSummarizer + ConversationStorageService + ConversationListViewModel + LocalCommandInterceptor。
- **LocalCommandInterceptor**：本地命令拦截器。40+ 条确定性系统指令（打开任务管理器/计算器/记事本/下载文件夹/设置、锁屏、关机重启、清空回收站等）在发送到 AI 前被正则+字典匹配拦截，本地 `Process.Start` 直接执行。`TryIntercept(input)` 返回 null（不是已知命令）或执行结果字符串。资源成本：仅拦截匹配时 ~1ms，空闲时零开销。
- **ChatViewModel 内建本地拦截**：`TryComputeLocally(input)` 在 SendInternalAsync 中位于 LocalCommandInterceptor 之后、AI 路由之前。处理纯数学表达式（DataTable.Compute）、日期时间查询（"今天几号""现在几点""今天星期几"等）。零 token 消耗。
- **ChatMessage**：继承 `ObservableObject`，Content 属性支持 `INotifyPropertyChanged` 供流式输出时 UI 实时更新。新增字段：ConversationId（多对话支持）、ImagePath/ImageBytes/HasImage（图片附件支持）、IsEditing/EditText（消息编辑支持）、CanRegenerate（重新生成支持，JsonIgnore）。
- **ChatView**：WPF 聊天界面（深色/浅色主题切换，Markdown 渲染、代码高亮、消息气泡、输入框、加载动画、取消按钮），无 DropShadowEffect。左侧 200px 可折叠 Sidebar（对话列表 + 搜索框 + 新建按钮）。消息气泡支持：图片缩略图（MaxWidth=300, MaxHeight=200）、编辑按钮（hover 显示，切换 TextBox）、重新生成按钮（最后一条 Assistant 消息）。粘贴图片按钮 + 待发图片预览。支持文件拖放（AllowDrop=true，DragEnter/Drop 处理）。Markdig.Wpf 解析 Markdown → FlowDocument。支持 Up/Down 箭头导航输入历史。所有颜色使用 DynamicResource 主题系统。
- **ChatHistoryService**：委托给 ConversationStorageService，接口不变（Load/Save/HasHistory）。Save 保存到当前活跃对话文件，Load 从活跃对话加载。
- **ConversationStorageService**：多对话文件 I/O 服务（`conversations/index.json` + `{guid}.json`）。Index CRUD（创建/重命名/删除/更新元数据），消息 I/O（独立文件保存/加载），全文搜索（`SearchAllConversations(query)`，string.Contains，不区分大小写），自动迁移旧 `chat_history.json` 到默认对话。资源成本：仅读写时触发磁盘 I/O（按需消耗）。
- **ConversationListViewModel**：对话列表 VM（ObservableCollection + 创建/重命名/删除/切换命令）。搜索防抖（300ms DispatcherTimer），`ConversationSwitched` 事件通知 ChatViewModel。新建对话自动标题（取第一条用户消息前 20 字）。依赖 ConversationStorageService。
- **LocalModelService**：封装 LLamaSharp 加载 Qwen2.5-0.5B-Instruct GGUF 本地模型。`InferAsync(prompt, maxTokens?, systemPrompt?, ct)` 提供单轮无状态推理。延迟初始化（首次调用才加载），`SemaphoreSlim(1,1)` 保证线程安全，`IDisposable` 释放 `LLamaWeights`/`LLamaContext`。模型获取 4 层优先级：%APPDATA% → 打包目录 → exe 旁边目录（单文件发布）→ 多镜像自动下载（model_sources.json 配置，带进度报告）。公开属性：`ModelDirectory`, `ModelFilePath`, `ModelFileExists`, `ModelFileSize`。公开方法：`DownloadModelAsync(progress, ct)` 强制重新下载，`UploadModelAsync(sourcePath, progress)` 复制用户 .gguf 到模型目录。资源成本：首次加载 ~550MB 内存（模型 + KV Cache），空闲时仅内存驻留，无 CPU 消耗。
- **ModelRoutingService**：3 层漏斗精准模型路由。L1 快速预判（极短/明显需工具）→ L2 本地 Qwen 0.5B 语义意图分类（conversation/question → 本地 / action/creation/system → 远程）→ L3 语义质量评估（不合格自动回退远程）。本地分类阶段 MaxTokens=10，约 1-2s。资源成本：按需消耗，空闲时零开销。
- **TokenUsageService**：Token 用量统计（~4 chars/token 估算）。记录每轮输入/输出的 token 消耗，按月分桶，异步持久化到 `%APPDATA%\PersonalAssistant\token_usage.json`。自动修剪 12 个月前的旧数据。资源成本：仅记录和写盘时消耗，空闲时零开销。
- **ConversationSummarizer**：对话摘要器。对话超过 30 轮时，提取最旧 10 轮调用本地模型生成摘要，注入系统提示词作为上下文。`SummarizeAsync(messages)` 返回摘要文本，`LatestSummary` 暴露最新摘要供系统提示词注入。资源成本：仅触发时消耗（~2-3s 本地推理），空闲时零开销。
- **ChatExportService**：对话导出服务。将对话历史导出为 Markdown 文件（SaveFileDialog 选择路径）。资源成本：仅导出时消耗，空闲时零开销。
- **工具调用确认**：高危工具（run_shell, write_file, delete_workflow, delete_schedule）执行前弹窗确认。`IDangerousToolPolicy.DangerConfirmation` 委托在 ChatViewModel 中设置，通过 `Dispatcher.Invoke` 封送到 UI 线程显示 MessageBox。确认机制为本地代码逻辑，零 token 消耗。

### Plugins（插件化工具模块，8 个自包含插件）

> 每个插件实现 `IToolPlugin` 接口，通过 PluginAggregator 自动聚合。
> 录制由 PluginAggregator 透明处理（系统工具录制，管理工具和 local_llm 不录制）。

| 插件 | 文件 | 工具数 | 说明 |
|------|------|--------|------|
| **SystemToolsPlugin** | `Plugins/SystemTools/` | 13 | read_file, write_file, list_files, run_shell, run_command, find_app, send_keys, type_text, window_info, focus_window, read_clipboard, write_clipboard, search_files。Win32 P/Invoke 提取到独立 `Win32Native.cs` |
| **WebToolsPlugin** | `Plugins/WebTools/` | 2 | web_fetch, web_search (DuckDuckGo, HtmlAgilityPack 解析) |
| **SystemInfoPlugin** | `Plugins/SystemInfoPlugin/` | 2 | system_info, screenshot + Windows OCR |
| **ChatToolsPlugin** | `Plugins/ChatToolsPlugin/` | 2 | clear_chat (通过 PluginSharedState.RaiseClearChat 事件), notify |
| **SchedulerPlugin** | `Plugins/SchedulerPlugin/` | 3 | add_schedule, list_schedules, delete_schedule |
| **WorkflowPlugin** | `Plugins/WorkflowPlugin/` | 4 | list_workflows, run_workflow, delete_workflow, save_workflow |
| **LocalLLMPlugin** | `Plugins/LocalLLMPlugin/` | 1 | local_llm (Qwen2.5-0.5B 本地推理) |
| **KnowledgeBasePlugin** | `Plugins/KnowledgeBasePlugin/` | 1 | knowledge_search (搜索本地已索引文档，TF-IDF + 余弦相似度) |
| **PluginManagementWindow** | `Plugins/` | - | 插件可视化管理：查看、启用/禁用、删除、导入。Transient 生命周期。从托盘右键菜单"插件管理"打开 |
| **PluginMarketplaceWindow** | `Plugins/` | - | 插件市场：搜索 GitHub Gists 上的社区插件（`[personal-assistant-plugin]` 标签），一键下载安装。Transient 生命周期 |

### Core（平台核心）

| 组件 | 位置 | 说明 |
|------|------|------|
| **PluginAggregator** | `Core/Services/` | 中心枢纽：实现 IToolPluginHost + IDangerousToolPolicy。通过 DI `IEnumerable<IToolPlugin>` 自动发现所有内置插件 + PluginLoader 加载外部插件。外部插件优先。双列表（_allPlugins / _activePlugins），AllPlugins 属性暴露完整列表供管理窗口。内建 WorkflowRecorder 透明录制 + 危险工具确认策略。GetAllTools 带缓存（插件变更自动失效），ExecuteToolStepAsync 每插件独立 try-catch，RefreshActivePlugins() 支持运行时免重启同步 |
| **PluginSharedState** | `Core/Services/` | 插件间共享状态（PendingSuggestion, OnClearChat 事件 + RaiseClearChat 触发方法） |
| **PluginBase** | `Core/Plugins/` | 外部插件基类（PluginBase + PluginToolDefinition + PluginParameter DTO），零外部依赖 |
| **PluginLoader** | `Core/Plugins/` | Roslyn 编译 %APPDATA%\PersonalAssistant\Plugins\*.cs → 反射发现 PluginBase 子类 → 实例化 |
| **ExternalPluginAdapter** | `Core/Plugins/` | PluginBase → IToolPlugin 桥接，运行时 AIFunctionFactory.Create 生成 AIFunction[]。暴露 SourcePlugin 属性供管理窗口枚举 |
| **PluginFileWatcher** | `Features/Plugins/` | FileSystemWatcher 监控外部插件目录 `*.cs` 文件变更（500ms 防抖），触发 PluginFileChanged 事件供热重载。资源成本：OS 事件驱动，空闲时零开销 |
| **PluginMarketplaceService** | `Features/Plugins/Services/` | GitHub Gists API 搜索社区插件（按 `[personal-assistant-plugin]` 标签过滤），结果缓存 1 小时。支持一键下载安装。资源成本：HTTP 按需消耗，1h 缓存 |
| **PluginStateService** | `Infrastructure/Common/Services/` | 插件启用/禁用状态持久化（HashSet + JSON），零定时器/线程，空闲时零开销 |

### Workflow（学习能力）
- **WorkflowRecorder**：录制每轮对话中的工具调用序列（工具名列表）。线程安全（lock 保护），录制由 PluginAggregator 透明处理。
- **PatternDetector**：最近 50 轮环形缓冲 + 序列匹配（≥4 次重复 + ≥3 工具序列）→ 触发建议。已建议的序列不重复提示（`_shownKeys` HashSet）。建议通过 InfoBar 展示，不占用聊天消息。
- **WorkflowStorageService**：JSON 持久化到 `%APPDATA%\PersonalAssistant\workflows\` 目录
- **WorkflowExecutorService**：本地回放已保存工作流，不调用 AI，通过 `IToolPluginHost.ExecuteToolStepAsync()` 执行

### Clipboard（智能剪贴板）
- **ClipboardMonitor**：Win32 `AddClipboardFormatListener` 监听剪贴板变化（OS 消息驱动，零轮询）。200ms 防抖，COMException 容错（剪贴板被其他进程锁定时静默跳过）。本地启发式分类算法：URL（正则匹配协议头）→ Path（盘符+路径验证）→ Number（纯数字/逗号/点号）→ Code（符号密度>12%且≥3种符号族）→ Text（含字母）。`Initialize(IntPtr hwnd)` 两阶段初始化（构造参数空 → OnSourceInitialized 挂载 HWND）。`LatestClipboardText`（截断 5K）, `LatestClipboardType`, `SuppressNextUpdate()`（避免写剪贴板反馈循环）。`ClipboardChanged` 事件（后台线程触发，订阅者需自行封送 UI）。IDisposable：RemoveClipboardFormatListener + 移除 Hook。资源成本：OS 消息驱动，空闲时零 CPU，~200 bytes 常驻内存。
- **ClipboardToolHelper**：剪贴板工具方法静态辅助类。提供：文件路径操作（复制完整路径/文件名无扩展名、在终端打开、在资源管理器打开）、文本统计（字符/单词/行/字节数）、Base64 编解码、JSON 格式化/压缩、时间戳转换（Unix 秒/毫秒 → 本地时间）、颜色检测与解析（HEX/RGB）、数学表达式求值（DataTable.Compute）、剪贴板图片 OCR（Windows 内置引擎，零 token）。全部零 token 纯本地执行。资源成本：仅调用时消耗 CPU，空闲时零开销。
- **ContextMenuPopup**：智能剪贴板上下文菜单弹窗（WindowStyle=None, AllowsTransparency=True, Topmost=True, Width=240）。根据内容类型动态展示操作按钮（纯代码逻辑，零 token）。子类型自动检测：Base64（解码/复制）、JSON（格式化/压缩/复制）、时间戳（转换）、颜色值（复制 HEX/RGB + 色块预览）、数学表达式（计算/复制结果）、文件路径（打开/复制完整路径/复制文件名/在终端打开/打开所在目录）。URL类型：在浏览器打开、复制链接、生成二维码（在线API）、总结/翻译网页。Code类型：解释/优化/查找错误 + 文本统计。Text类型：文本统计、Base64编码、总结/翻译/搜索。Number类型：大写转换、汇率换算。所有类型通用：颜色预览方块（HEX/RGB时显示实色块）、内联结果面板（文本统计/Base64解码/JSON格式化等结果在弹窗内展示，不关闭弹窗）。底部按钮：快速便签（StickyNoteWindow）、OCR识别（剪贴板图片 → Windows 内置 OCR → 显示结果并复制）、桌面小组件（WidgetPanel入口）。PositionNear 定位到悬浮窗左侧。OnDeactivated/Escape 自动关闭。所有颜色使用 DynamicResource 主题适配。资源成本：仅在显示时消耗（窗口渲染），隐藏时零开销。
- **StickyNoteWindow**：快速便签窗口（无边框、置顶、可拖动、黄色便签风格）。文本框自动保存到 `%APPDATA%\PersonalAssistant\sticky_note.txt`。清空按钮 + 关闭按钮。Escape 键关闭。资源成本：仅在显示时消耗（窗口渲染），隐藏时零开销。

### KnowledgeBase（知识库搜索）
- **KnowledgeBaseService**：全文检索引擎，支持 .md/.txt/.pdf 文件。中文分词（逐字）+ 英文分词（空格），TF-IDF + 余弦相似度排序，Top-K 结果返回。索引持久化到 `%APPDATA%\PersonalAssistant\knowledge_base\index.json`。`IndexDirectoryAsync(dir, progress?)` 异步索引目录，`Search(query, topK=5)` 搜索。资源成本：仅索引和搜索时消耗 CPU，空闲时零开销。
- **DocumentChunk**：文档分块模型（512 字符重叠分块），`KnowledgeBaseIndex` 包含完整索引元数据。
- **KnowledgeBasePlugin**：1 个 AI 工具 `knowledge_search(query)`，AI 可通过它搜索用户本地文档。提示词片段指导 AI 优先使用此工具回答文档相关问题。

### Widgets（桌面小组件）
- **WidgetPanel**：无边框透明置顶窗口，位于悬浮窗左侧。根据 WidgetConfig 按需加载卡片（Weather/Todo/SystemStatus），`PositionNear(target)` 跟随悬浮窗定位。资源成本：仅在显示时消耗，隐藏时零开销。
- **WidgetCard**：可复用卡片容器（UserControl），含标题 + ContentPresenter。
- **WeatherWidget**：天气显示，wttr.in 免费 API 查询（格式 `%t+%C`），30 分钟缓存，失败降级为 `"--"`。资源成本：首次加载 1 次 HTTP，之后 30min 缓存。
- **TodoWidget**：简易待办列表，输入框 + Enter/按钮添加，持久化到 `%APPDATA%\PersonalAssistant\todos.json`。
- **SystemStatusWidget**：系统资源监控，PerformanceCounter 读取 CPU 使用率 + GC.GetTotalMemory 读取托管内存，5s 刷新。资源成本：5s 定时器仅在显示时运行，隐藏时停止并 Dispose。
- **WidgetConfigService**：小组件开关配置持久化到 `%APPDATA%\PersonalAssistant\widget_config.json`。

### Notifications（通知历史）
- **NotificationHistoryWindow**：通知历史窗口，显示最近 50 条托盘通知（标题、来源、时间戳、内容）。Transient 生命周期，从托盘右键菜单"通知历史"打开。
- **NotificationRecord**：通知记录 POCO（Title, Message, Timestamp, Source）。

### Scheduler（定时任务）
- **SchedulerService**：System.Threading.Timer 30s 间隔检查，SemaphoreSlim 防重入，匹配 HH:mm → 检查 LastRunDate → IToolPluginHost.ExecuteToolStepAsync → 更新 LastRunDate。任务列表内存缓存（5 分钟刷新），避免每次 Tick 读取磁盘。IDisposable 清理 Timer + Semaphore。依赖 IToolPluginHost（不再依赖 ChatAgentService）
- **SchedulerStorageService**：JSON 持久化到 `%APPDATA%\PersonalAssistant\schedules\` 目录
- **ScheduledTask**：POCO 模型（Name, TimeOfDay, ToolName, ToolArgs, IsEnabled, CreatedAt, LastRunDate）

### AI 工具方法

> 斜杠命令已统一为 AI 工具层（8 个插件）。用户用自然语言操作，AI 自动调用对应工具。
> 仅 `/clear` 保留本地拦截（ChatViewModel），零 token 消耗。

| AI 工具 | 所属插件 | 说明 |
|---------|---------|------|
| `read_file(path)` | SystemTools | 读取文本文件内容 |
| `write_file(path,content)` | SystemTools | 写入文本到文件（高危确认） |
| `list_files(path?)` | SystemTools | 列出目录内容 |
| `search_files(pattern,dir?)` | SystemTools | 递归搜索文件，懒加载，100条上限，10s超时 |
| `run_shell(command)` | SystemTools | 执行 PowerShell 命令（高危确认） |
| `run_command(exe,args?)` | SystemTools | 启动程序/打开文件/URL |
| `find_app(keyword)` | SystemTools | 搜索开始菜单已安装程序 |
| `send_keys(input)` | SystemTools | 按键组合或文本输入（SendInput） |
| `type_text(text)` | SystemTools | 直接输入文本到前台应用 |
| `window_info()` | SystemTools | 焦点窗口 + 可见窗口列表 |
| `focus_window(title)` | SystemTools | 根据标题聚焦窗口 |
| `read_clipboard()` | SystemTools | 读取 Windows 剪贴板 |
| `write_clipboard(text)` | SystemTools | 写入 Windows 剪贴板 |
| `web_fetch(url)` | WebTools | 抓取网页文本内容 |
| `web_search(query)` | WebTools | DuckDuckGo 搜索 Top 10 |
| `system_info(cat?)` | SystemInfo | 系统状态：内存/磁盘/进程/电池 |
| `screenshot()` | SystemInfo | 截屏 + Windows 本地 OCR |
| `clear_chat()` | ChatTools | 清空对话历史 + 重置模式检测器 |
| `notify(title,msg)` | ChatTools | 托盘气泡通知 |
| `add_schedule(time,tool,args)` | Scheduler | 创建每日定时任务 |
| `list_schedules()` | Scheduler | 列出所有定时任务 |
| `delete_schedule(name)` | Scheduler | 删除定时任务（高危确认） |
| `list_workflows()` | Workflow | 列出已保存工作流 |
| `run_workflow(name)` | Workflow | 本地回放工作流，不经过 AI |
| `delete_workflow(name)` | Workflow | 删除工作流（高危确认） |
| `save_workflow(name)` | Workflow | 保存最近检测到的重复模式为工作流 |
| `local_llm(prompt)` | LocalLLM | 本地小模型推理，零远程 token |
| `knowledge_search(query)` | KnowledgeBase | 搜索本地已索引文档，TF-IDF + 余弦相似度 |

| 本地命令 | 触发 | 作用 |
|---------|------|------|
| `/clear` | ChatViewModel | 清空消息+历史+重置模式检测器（零 token） |

### MainWindow
- **全局快捷键（可配置）**：
  - `Alt+Space`（默认）：切换主窗口显示/隐藏（Win32 RegisterHotKey + WndProc hook），无后台轮询开销
  - `Ctrl+Alt+Space`（默认）：在任何应用中选中文本后按下 → 模拟 Ctrl+C 复制 → 恢复原始剪贴板 → 显示主窗口并填入选中文本，用户可添加指令后发送给 AI
  - 快捷键可在设置窗口中自定义（Modifiers + Key），通过 `UserSettingsService` 持久化
- 热键注册失败时通过托盘气泡通知用户（如被 PowerToys 占用）
- 关闭/最小化主窗口 → 显示悬浮窗（右下角，始终置顶，正弦浮动动画）

### Mascot
- **MascotWindow**：卡通机器人悬浮窗，纯 XAML 绘制（椭圆/矩形/Path 拼合）
- **鼠标交互：** 眼球追踪鼠标（25fps 节流）、悬停放大 1.12x + 天线变青、点击压缩弹跳、可拖动
- 左键点击悬浮窗（未拖动）→ 隐藏人偶 → 恢复主窗口并聚焦输入框
- 右键点击悬浮窗 → 剪贴板有内容则弹出智能上下文菜单（ContextMenuPopup），无内容则切换 WidgetPanel 桌面小组件面板（显示/隐藏）
- 人偶隐藏时浮动动画自动暂停，显示时恢复（省 CPU）

### Settings
- **SettingsWindow**：AI 模型配置 + 开机自启动 + 主题切换 + 快捷键自定义 + 知识库管理 + 模型管理，深色/浅色主题适配
- **模型管理**：设置窗口新增模型管理区域，显示模型状态（就绪/未安装 + 文件大小），支持打开模型目录、上传本地 .gguf 文件、从镜像源下载默认模型。下载/上传带进度显示，窗口关闭时自动取消进行中的操作。
- 配置保存在 `%APPDATA%\PersonalAssistant\settings.json`
- 从托盘右键菜单"设置"打开
- **配置项**：API Key（显示/隐藏切换）、提供商预设（DeepSeek/Zhipu GLM/自定义）、模型名称、API 端点、连接测试、开机自启动、自定义系统提示词（多行输入，留空使用默认）、深色/浅色主题、快捷键组合（HotkeyCaptureBox 控件）、知识库目录选择 + 一键索引（带进度）

### Tray
- **TrayService**：系统托盘图标（代码绘制蓝紫渐变 "AI" 图标）+ 右键菜单（显示主窗口、设置、插件管理、导出对话、通知历史、切换主题、退出）。通知历史保留最近 50 条（`ObservableCollection<NotificationRecord>`），气泡通知自动记录。`ShowNotification(title, message)` 统一通知入口，>256 字符自动截断
- **UserSettingsService**：管理用户级配置（API Key/Model/Endpoint/AutoStart/HotkeyModifiers/HotkeyKey/CustomSystemPrompt/IsDarkTheme），含注册表操作。配置文件损坏时记录警告并降级为默认值。新增快捷键配置属性（HotkeyModifiers, HotkeyKey, SelectTextModifiers, SelectTextKey）、主题属性（IsDarkTheme）和自定义系统提示词属性（CustomSystemPrompt，null=使用默认）

## 性能约束 + 低功耗设计（强制）

> 桌面助手类应用须长期驻留后台运行，所有功能开发必须优先考虑资源占用最小化。

### 性能规则

| 规则 | 说明 |
|------|------|
| **禁止 DropShadowEffect** | 软件渲染每帧重绘，极度消耗 GPU/CPU。用半透明 Shape 代替阴影，用 Fill 颜色变化代替发光 |
| **消息上限 200** | ChatAgentService AgentSession 管理 + ChatViewModel 各限 200 条，超出自动修剪最旧消息，防止内存无限增长 |
| **动画对象复用** | EasingFunction 等无状态对象必须缓存为实例字段，不在热点路径 `new` |
| **鼠标事件节流** | 高频事件（MouseMove）必须节流（Stopwatch 控制间隔），不超过 30fps |
| **隐藏时停动画** | 窗口/控件隐藏时必须停止所有 RepeatBehavior.Forever 动画 |
| **Brush/Material 冻结** | 可跨线程共享的 Brush/Pen 等对象必须调用 `Freeze()` 后缓存为 `static readonly`，避免每帧分配 |

### 低功耗设计原则

| 规则 | 说明 |
|------|------|
| **空闲时 CPU 趋近零** | 无用户交互时，进程 CPU 占用必须趋近 0%。不得有持续运行的繁忙循环或高频轮询 |
| **事件驱动，禁止轮询** | 所有逻辑必须基于事件触发（用户输入、系统事件、异步回调），禁止 `while(true)` / 定时器轮询状态 |
| **禁止忙等 (Busy-Wait)** | 禁止 `SpinWait`、空循环等待条件满足。等待必须用 `await` / `WaitHandle` / `CancellationToken` |
| **后台定时器间隔 ≥ 1秒** | 非关键后台任务（心跳、状态刷新）的 Timer 间隔不得低于 1 秒，且空闲时可进一步降频 |
| **禁止无界线程/Task 创建** | 禁止循环内 `Task.Run` 或 `new Thread()`。并发任务必须通过 `SemaphoreSlim` / `Channel` 限流 |
| **Dispose 必须到位** | 所有 `IDisposable` / `IAsyncDisposable` 资源必须在不再需要时立即释放。Timer、Subscription、CTS 必须由 owning 类在销毁时清理 |
| **内存稳态要求** | 长时间运行后（>1小时），进程内存应在稳态范围内波动，不得持续单向增长。定期操作（如消息列表）需有上限截断 |
| **新功能必评估资源成本** | 每新增一个功能模块，必须在代码注释或 commit 中说明其对 CPU/内存的持续影响（近似零 / 按需消耗 / 持续消耗 xMB） |

### 插件系统资源评估
- **IToolPlugin 实例（8个）**：~16个单例对象，无定时器/线程，空闲时零开销
- **PluginAggregator**：1个单例，持有 2 个 List（_allPlugins + _activePlugins），GetAllTools 带缓存（零分配直到失效），ExecuteToolStepAsync 线性扫描 O(n)，每插件独立 try-catch
- **PluginLoader**：启动时一次性 ~50-200ms CPU（Roslyn 编译），之后零开销
- **PluginFileWatcher**：1个 FileSystemWatcher，OS 事件驱动，空闲时零开销
- **PluginMarketplaceService**：HTTP 按需消耗，1h 缓存，空闲时零开销
- **录制包装**：WorkflowRecorder 引用，每次工具调用一次 RecordStep()
- **DI 扫描**：启动时 ~1ms 反射扫描，零运行时开销

### 学习能力资源评估
- **WorkflowRecorder**：近似零开销（lock 内 O(1) List 追加，每轮对话清空）
- **PatternDetector**：按需消耗（仅在每轮结束时做 O(n) 序列匹配，n ≤ 50）
- **WorkflowStorageService**：按需消耗（仅读写时触发磁盘 I/O）
- **WorkflowExecutorService**：按需消耗（仅 run_workflow 调用时执行，不调 AI）

### 本地模型资源评估
- **LocalModelService**：首次加载 ~550MB 内存（模型 + 1024 context KV Cache），空闲时零 CPU。推理时 CPU 按需消耗（~0.5B 参数，单轮 <5s），单线程 SemaphoreSlim 限流。

### 定时任务资源评估
- **SchedulerService**：30s 定时器 Tick 检查（O(1) 内存缓存比较），5 分钟刷新一次磁盘
- **SchedulerStorageService**：按需消耗（仅 Tick 匹配时读磁盘 + 执行后写 LastRunDate）

### 知识库资源评估
- **KnowledgeBaseService**：启动时 `LoadIndex()` 一次性加载索引到内存，空闲时仅内存驻留。索引时 CPU 按需消耗（文件读取 + 分词），搜索时 O(n) 余弦相似度计算（n = 分块数）

### 剪贴板资源评估
- **ClipboardMonitor**：OS 消息驱动，空闲时零 CPU，~200 bytes 常驻内存（仅 latestText 字符串 + type 枚举 + lock 对象）。分类算法纯本地启发式（正则/字符串匹配），零 token 消耗。剪贴板变化时一次性 CPU 消耗 <1ms。
- **ContextMenuPopup**：仅在显示时消耗（窗口渲染 + 几个 Button 控件），隐藏时零开销。每次弹出时生成按钮（一次性 UI 分配，关闭后 GC 回收）。

### 小组件资源评估
- **WidgetPanel**：仅在显示时消耗（窗口渲染），隐藏时零开销
- **SystemStatusWidget**：5s Timer 仅在显示时运行，隐藏时 Dispose 停止
- **WeatherWidget**：首次 1 次 HTTP，30min 缓存，之后零开销
- **TodoWidget**：仅读写时触发磁盘 I/O，空闲时零开销

### 主题系统资源评估
- **ThemeService**：启动时一次性切换 ResourceDictionary（~1ms），之后零开销。主题切换时重新加载 XAML 资源（按需消耗）

## 配置约定

- AI 配置通过托盘 → "设置" 窗口修改，保存在用户目录 `%APPDATA%\PersonalAssistant\settings.json`
- `DEEPSEEK_API_KEY` 环境变量可作为替代，优先级高于配置文件
- `appsettings.json`（不入库）：仅含 `Serilog` 日志配置
- `appsettings.template.json`（入库）：Serilog 占位符模板
- 开机自启动：通过注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PersonalAssistant` 管理
- 工作流数据：保存在 `%APPDATA%\PersonalAssistant\workflows\` 目录
- 定时任务数据：保存在 `%APPDATA%\PersonalAssistant\schedules\` 目录
- 本地模型文件：放在 `%APPDATA%\PersonalAssistant\models\` 目录（*.gguf，不入库）
- 模型下载配置：`Assets/model_sources.json`（入库），含多镜像 URL 和回退顺序，可独立更新无需改代码
- 单文件发布：`PublishSingleFile=true`，GGUF 模型 `ExcludeFromSingleFile=true` 独立于 exe。发布后 exe 旁边 `Assets/` 目录放置模型文件即可分发
- 插件启用状态：保存在 `%APPDATA%\PersonalAssistant\plugin_state.json`（不入库）
- Token 用量数据：保存在 `%APPDATA%\PersonalAssistant\token_usage.json`（不入库）
- 知识库索引：保存在 `%APPDATA%\PersonalAssistant\knowledge_base\index.json`（不入库）
- 小组件配置：保存在 `%APPDATA%\PersonalAssistant\widget_config.json`（不入库）
- 待办事项：保存在 `%APPDATA%\PersonalAssistant\todos.json`（不入库）
- 快速便签：保存在 `%APPDATA%\PersonalAssistant\sticky_note.txt`（不入库）
- 对话数据：保存在 `%APPDATA%\PersonalAssistant\conversations\` 目录（index.json + {guid}.json，不入库）
- 对话图片：保存在 `%APPDATA%\PersonalAssistant\conversations\{guid}_images\` 目录（不入库）

## 启动流程

1. `App.xaml.cs` → `Host.CreateDefaultBuilder` + Serilog + DI 注册（Scrutor 3 种扫描 + 手动注册，ExternalPluginAdapter 排除扫描）
2. `UserSettingsService` 从 `%APPDATA%` 加载配置
3. `ThemeService.Initialize()` 应用主题（深色/浅色，Default 暗色），必须在 MainWindow 之前执行
4. `MainWindow` 加载 → 注册可配置全局热键 → 初始化 ClipboardMonitor（OS 消息驱动）→ 显示 `ChatView`
5. `TrayService` 初始化托盘图标 + 右键菜单
6. `SchedulerService` 初始化后台定时任务调度（30s 间隔）
7. `KnowledgeBaseService.LoadIndex()` 加载已有知识库索引
8. 后台线程预热本地模型（`Task.Run` + `EnsureModelAvailableAsync()`，不阻塞 UI）
9. 用户发送消息（普通文本或 `/clear`）→ `ChatViewModel.SendAsync()` → 离线检测 → 模型路由 → `ChatAgentService.SendMessageStreaming()` → MAF 工具循环 → DeepSeek API（流式输出）
10. 关闭窗口 → 隐藏主窗口 → 显示卡通人偶浮动窗
11. 最小化窗口 → 隐藏主窗口（不在任务栏） → 显示人偶
12. 点击人偶 / 托盘"显示主窗口" → 隐藏人偶 → 恢复主窗口

## 项目结构

```
PersonalAssistant/
├── App.xaml / App.xaml.cs              # 应用入口 + DI（3 种扫描 + 手动注册，ExternalPluginAdapter 排除）
├── MainWindow.xaml / MainWindow.xaml.cs # FluentWindow + TitleBar + ChatView + 可配置全局热键
├── Core/                               # 平台契约 + 聚合器 + 外部插件系统
│   ├── Interfaces/
│   │   ├── IToolPlugin.cs              # 插件契约
│   │   ├── IToolPluginHost.cs          # 聚合器接口
│   │   └── IDangerousToolPolicy.cs     # 危险操作确认策略
│   ├── Plugins/
│   │   ├── PluginBase.cs               # 外部插件基类 + DTO
│   │   ├── PluginLoader.cs             # Roslyn 编译加载器
│   │   └── ExternalPluginAdapter.cs    # PluginBase → IToolPlugin 桥接
│   └── Services/
│       ├── PluginAggregator.cs         # 中心枢纽（IToolPluginHost + IDangerousToolPolicy）
│       └── PluginSharedState.cs         # 插件间共享状态
├── Features/
│   ├── Chat/
│   │   ├── Models/                      # ChatMessage(CnvId/ImgPath/ImgBytes/HasImage/IsEditing/EditText/CanRegenerate),
│   │   │                                # ChatSettings, NotificationRecord, TokenUsageStats,
│   │   │                                # ConversationInfo(Id/Title/CreatedAt/UpdatedAt/MessageCount),
│   │   │                                # ConversationSearchResult(ConvId/Title/Excerpt/MessageIndex),
│   │   │                                # ImageAttachment(Bytes/MediaType/Width/Height), 枚举
│   │   ├── Services/
│   │   │   ├── ChatAgentService.cs      # MAF 封装 (~300行) + ChatSystemPrompt.cs
│   │   │   ├── LocalModelService.cs      # 本地 LLM 推理（4层模型路径 + 上传/下载）
│   │   │   ├── LocalCommandInterceptor.cs # 本地命令拦截器（40+ 条确定性系统指令，零 token）
│   │   │   ├── ModelRoutingService.cs    # 自动模型路由（本地/远程）
│   │   │   ├── TokenUsageService.cs      # Token 用量统计 + 持久化
│   │   │   ├── ConversationSummarizer.cs # 对话摘要生成（本地 LLM）
│   │   │   ├── ConversationStorageService.cs # 多对话 I/O（index.json + {id}.json，搜索，迁移）
│   │   │   └── ChatExportService.cs      # 对话导出（Markdown）
│   │   ├── ViewModels/
│   │   │   ├── ChatViewModel.cs          # 聊天主 VM（+ 编辑/重新生成/图片粘贴/文件拖拽）
│   │   │   └── ConversationListViewModel.cs # 对话列表 VM（创建/重命名/删除/切换/搜索防抖）
│   │   └── Views/ChatView.xaml/.cs
│   ├── Plugins/                         # 自包含插件模块 (8个) + 管理/市场窗口
│   │   ├── SystemTools/                 # 13 工具: read_file, write_file, ...
│   │   │   ├── SystemToolsPlugin.cs
│   │   │   ├── SystemToolMethods.cs
│   │   │   └── Win32Native.cs           # Win32 P/Invoke (static helper)
│   │   ├── WebTools/                    # 2 工具: web_fetch, web_search
│   │   ├── SystemInfoPlugin/            # 2 工具: system_info, screenshot
│   │   ├── ChatToolsPlugin/             # 2 工具: clear_chat, notify
│   │   ├── SchedulerPlugin/             # 3 工具: add/list/delete_schedule
│   │   ├── WorkflowPlugin/              # 4 工具: list/run/delete/save_workflow
│   │   ├── LocalLLMPlugin/              # 1 工具: local_llm
│   │   ├── KnowledgeBasePlugin/         # 1 工具: knowledge_search
│   │   ├── Services/
│   │   │   └── PluginMarketplaceService.cs # GitHub Gists 插件市场
│   │   ├── PluginFileWatcher.cs         # 外部插件热重载监控
│   │   ├── PluginManagementWindow.xaml  # 插件管理窗口 (View)
│   │   ├── PluginManagementWindow.xaml.cs
│   │   ├── PluginMarketplaceWindow.xaml # 插件市场窗口 (View)
│   │   └── PluginMarketplaceWindow.xaml.cs
│   ├── Workflow/
│   │   ├── Models/                      # ToolCallRecord, WorkflowDefinition, PatternMatch
│   │   └── Services/                    # WorkflowRecorder, PatternDetector,
│   │                                    # WorkflowStorageService, WorkflowExecutorService
│   ├── Scheduler/
│   │   ├── Models/                      # ScheduledTask
│   │   └── Services/                    # SchedulerService, SchedulerStorageService
│   ├── KnowledgeBase/
│   │   ├── Models/                      # DocumentChunk, KnowledgeBaseIndex
│   │   └── Services/                    # KnowledgeBaseService (TF-IDF 全文检索)
│   ├── Widgets/
│   │   ├── Models/                      # WidgetConfig
│   │   ├── Services/                    # WidgetConfigService
│   │   ├── WidgetPanel.xaml/.cs         # 小组件面板窗口
│   │   ├── WidgetCard.xaml/.cs          # 可复用卡片容器
│   │   ├── WeatherWidget.xaml/.cs       # 天气小组件
│   │   ├── TodoWidget.xaml/.cs          # 待办小组件
│   │   └── SystemStatusWidget.xaml/.cs  # 系统状态小组件
│   ├── Clipboard/
│   │   ├── Models/                      # ClipboardContentType, ClipboardSuggestion(InlineResult)
│   │   ├── Services/                    # ClipboardMonitor (Win32 剪贴板监听 + 分类),
│   │   │                                # ClipboardToolHelper (路径/文本/Base64/JSON/时间戳/颜色/数学/OCR静态工具)
│   │   └── Views/                       # ContextMenuPopup (增强:子类型检测+内联结果+颜色预览+OCR+便签),
│   │                                    # StickyNoteWindow (快速便签窗口，黄色便签风格，自动保存)
│   ├── Notifications/
│   │   ├── NotificationHistoryWindow.xaml/.cs  # 通知历史窗口
│   ├── Mascot/
│   │   ├── MascotWindow.xaml            # XAML 形状绘制的机器人（无 DropShadowEffect）
│   │   └── MascotWindow.xaml.cs         # 眼球追踪、悬停、点击弹跳、拖拽、右键 WidgetPanel
│   └── Settings/
│       ├── SettingsWindow.xaml          # AI 配置 + 主题 + 快捷键 + 知识库 + 模型管理 + 开机自启动
│       └── SettingsWindow.xaml.cs
├── Infrastructure/Common/
│   ├── Helpers/                         # BrowserDetector, StartMenuScanner, AppIconGenerator, BytesToImageConverter,
│   │                                    # 通用转换器（BoolToVisibility, InverseBool, MarkdownToFlowDocument）
│   ├── Controls/                        # HotkeyCaptureBox (快捷键捕获控件)
│   ├── Services/                        # TrayService, UserSettingsService(CustomSystemPrompt), ChatHistoryService(委托),
│   │                                    # PluginStateService, ThemeService
│   └── Themes/                          # ThemeColors.xaml (语义色键), DarkTheme.xaml, LightTheme.xaml
├── docs/
│   ├── PLUGIN_DEV_GUIDE.md               # 外部插件开发手册
│   └── template/                         # 通用规范模板（git submodule）
├── appsettings.json                     # Serilog 日志配置 (不入库)
└── appsettings.template.json            # Serilog 模板 (入库)
```
