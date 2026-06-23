# PersonalAssistant - Claude 项目规范

> WPF 桌面 AI 助手，通过 DeepSeek API 提供 AI 对话+工具调用能力。
> 带卡通机器人悬浮窗 + 系统托盘，支持开机自启动。
> 基于 Microsoft Agent Framework (MAF) 实现 AI 对话、工具调用循环和流式输出。
> 插件化架构：工具方法自包含在 Plugins/ 目录，通过 IToolPlugin 接口热插拔。

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
| 聊天界面 | WPF + 深色科技风 Chat Bubble UI |
| MVVM | CommunityToolkit.Mvvm 8.4.0 |
| DI | Scrutor 6.1.0 自动扫描 |
| UI 框架 | WPF-UI 4.0.3 (FluentWindow, InfoBar, ProgressRing) |
| 日志 | Serilog 4.3.0 |
| 本地 LLM | LLamaSharp 0.27.0 + Qwen2.5-0.5B-Instruct GGUF (离线，零 token) |
| 用户配置 | `%APPDATA%\PersonalAssistant\settings.json` (不入库，每台电脑独立) |

## 架构：插件化工具系统

```
ChatAgentService (MAF 生命周期 + 流式输出, ~130行)
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
    ├── SystemToolsPlugin   (13 tools: read_file, write_file, ...)
    ├── WebToolsPlugin      (2 tools:  web_fetch, web_search)
    ├── SystemInfoPlugin    (2 tools:  system_info, screenshot)
    ├── ChatToolsPlugin     (2 tools:  clear_chat, notify)
    ├── SchedulerPlugin     (3 tools:  add/list/delete_schedule)
    ├── WorkflowPlugin      (4 tools:  list/run/delete/save_workflow)
    └── LocalLLMPlugin      (1 tool:   local_llm)
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
- **ChatAgentService**：~140 行，仅负责 MAF AIAgent 生命周期管理 + 流式输出 + 模式建议收集。工具方法全部移到 Plugins/，通过 `IToolPluginHost.GetAllTools()` 获取 AIFunction 数组，通过 `IToolPluginHost.GetAggregatedPrompt()` 获取聚合提示词。支持 `SendMessageStreaming(ct)` 流式输出和 `ClearHistoryAsync()` 清空历史+重置模式检测（异步事件处理，旧 Session 延迟 Dispose）。内建 PatternDetector 模式检测，通过 `CollectPatternSuggestion()` 返回建议。资源成本：仅消息发送时消耗，空闲时零开销（事件驱动）。
- **ChatSystemPrompt**：系统提示词构建器（从 ChatAgentService.Prompt.cs 提取）。基础提示词 + 聚合所有插件的提示词片段。线程安全锁保护缓存（双重检查锁定），插件片段不变则复用（零分配）。
- **ChatViewModel**：消息列表管理、流式 AI 响应更新、`/clear` 本地拦截、对话历史持久化（自动保存/恢复）、模式建议展示、InfoBar 错误显示、取消流式响应（CancelCommand）、输入历史记录（Up/Down 箭头导航，最近 50 条）。消息上限 200 条自动修剪。内建自动模型路由（ModelRoutingService），简单对话走本地模型（零 token），复杂/需工具走远程。依赖 ChatAgentService + IChatHistoryService + IDangerousToolPolicy + ModelRoutingService。
- **ChatMessage**：继承 `ObservableObject`，Content 属性支持 `INotifyPropertyChanged` 供流式输出时 UI 实时更新。
- **ChatView**：WPF 深色科技风聊天界面（Markdown 渲染、代码高亮、消息气泡、输入框、加载动画、取消按钮），无 DropShadowEffect。Markdig.Wpf 解析 Markdown → FlowDocument，AI 回复支持代码块语法高亮、列表、表格、加粗等。支持 Up/Down 箭头导航输入历史
- **ChatHistoryService**：对话历史持久化到 `%APPDATA%\PersonalAssistant\chat_history.json`（仅 UI 恢复，不回放 AI 会话），跳过 System 角色消息，最多 200 条。加载失败时记录 warning 日志并降级为空列表
- **LocalModelService**：封装 LLamaSharp 加载 Qwen2.5-0.5B-Instruct GGUF 本地模型。`InferAsync(prompt, maxTokens?, systemPrompt?, ct)` 提供单轮无状态推理。延迟初始化（首次调用才加载），`SemaphoreSlim(1,1)` 保证线程安全，`IDisposable` 释放 `LLamaWeights`/`LLamaContext`。模型获取优先级：%APPDATA% → 打包目录自动部署 → 多镜像自动下载（model_sources.json 配置，带进度报告）。资源成本：首次加载 ~550MB 内存（模型 + KV Cache），空闲时仅内存驻留，无 CPU 消耗。
- **ModelRoutingService**：3 层漏斗精准模型路由。L1 快速预判（极短/明显需工具）→ L2 本地 Qwen 0.5B 语义意图分类（conversation/question → 本地 / action/creation/system → 远程）→ L3 语义质量评估（不合格自动回退远程）。本地分类阶段 MaxTokens=10，约 1-2s。资源成本：按需消耗，空闲时零开销。
- **工具调用确认**：高危工具（run_shell, write_file, delete_workflow, delete_schedule）执行前弹窗确认。`IDangerousToolPolicy.DangerConfirmation` 委托在 ChatViewModel 中设置，通过 `Dispatcher.Invoke` 封送到 UI 线程显示 MessageBox。确认机制为本地代码逻辑，零 token 消耗。

### Plugins（插件化工具模块，7 个自包含插件）

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
| **PluginManagementWindow** | `Plugins/` | - | 插件可视化管理：查看、启用/禁用、删除、导入。Transient 生命周期。从托盘右键菜单"插件管理"打开 |

### Core（平台核心）

| 组件 | 位置 | 说明 |
|------|------|------|
| **PluginAggregator** | `Core/Services/` | 中心枢纽：实现 IToolPluginHost + IDangerousToolPolicy。通过 DI `IEnumerable<IToolPlugin>` 自动发现所有内置插件 + PluginLoader 加载外部插件。外部插件优先。双列表（_allPlugins / _activePlugins），AllPlugins 属性暴露完整列表供管理窗口。内建 WorkflowRecorder 透明录制 + 危险工具确认策略。GetAllTools 带缓存（插件变更自动失效），ExecuteToolStepAsync 每插件独立 try-catch，RefreshActivePlugins() 支持运行时免重启同步 |
| **PluginSharedState** | `Core/Services/` | 插件间共享状态（PendingSuggestion, OnClearChat 事件 + RaiseClearChat 触发方法） |
| **PluginBase** | `Core/Plugins/` | 外部插件基类（PluginBase + PluginToolDefinition + PluginParameter DTO），零外部依赖 |
| **PluginLoader** | `Core/Plugins/` | Roslyn 编译 %APPDATA%\PersonalAssistant\Plugins\*.cs → 反射发现 PluginBase 子类 → 实例化 |
| **ExternalPluginAdapter** | `Core/Plugins/` | PluginBase → IToolPlugin 桥接，运行时 AIFunctionFactory.Create 生成 AIFunction[]。暴露 SourcePlugin 属性供管理窗口枚举 |
| **PluginStateService** | `Infrastructure/Common/Services/` | 插件启用/禁用状态持久化（HashSet + JSON），零定时器/线程，空闲时零开销 |

### Workflow（学习能力）
- **WorkflowRecorder**：录制每轮对话中的工具调用序列（工具名列表）。线程安全（lock 保护），录制由 PluginAggregator 透明处理。
- **PatternDetector**：最近 50 轮环形缓冲 + 序列匹配（≥3 次重复）→ 触发建议。已建议的序列不重复提示（`_shownKeys` HashSet）
- **WorkflowStorageService**：JSON 持久化到 `%APPDATA%\PersonalAssistant\workflows\` 目录
- **WorkflowExecutorService**：本地回放已保存工作流，不调用 AI，通过 `IToolPluginHost.ExecuteToolStepAsync()` 执行

### Scheduler（定时任务）
- **SchedulerService**：System.Threading.Timer 30s 间隔检查，SemaphoreSlim 防重入，匹配 HH:mm → 检查 LastRunDate → IToolPluginHost.ExecuteToolStepAsync → 更新 LastRunDate。任务列表内存缓存（5 分钟刷新），避免每次 Tick 读取磁盘。IDisposable 清理 Timer + Semaphore。依赖 IToolPluginHost（不再依赖 ChatAgentService）
- **SchedulerStorageService**：JSON 持久化到 `%APPDATA%\PersonalAssistant\schedules\` 目录
- **ScheduledTask**：POCO 模型（Name, TimeOfDay, ToolName, ToolArgs, IsEnabled, CreatedAt, LastRunDate）

### AI 工具方法

> 斜杠命令已统一为 AI 工具层（7 个插件）。用户用自然语言操作，AI 自动调用对应工具。
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

| 本地命令 | 触发 | 作用 |
|---------|------|------|
| `/clear` | ChatViewModel | 清空消息+历史+重置模式检测器（零 token） |

### MainWindow
- **全局快捷键**：
  - `Alt+Space`：切换主窗口显示/隐藏（Win32 RegisterHotKey + WndProc hook），无后台轮询开销
  - `Ctrl+Alt+Space`：在任何应用中选中文本后按下 → 模拟 Ctrl+C 复制 → 恢复原始剪贴板 → 显示主窗口并填入选中文本，用户可添加指令后发送给 AI
- 热键注册失败时通过托盘气泡通知用户（如被 PowerToys 占用）
- 关闭/最小化主窗口 → 显示悬浮窗（右下角，始终置顶，正弦浮动动画）

### Mascot
- **MascotWindow**：卡通机器人悬浮窗，纯 XAML 绘制（椭圆/矩形/Path 拼合）
- **鼠标交互：** 眼球追踪鼠标（25fps 节流）、悬停放大 1.12x + 天线变青、点击压缩弹跳、可拖动
- 点击悬浮窗（未拖动）→ 隐藏人偶 → 恢复主窗口并聚焦输入框
- 人偶隐藏时浮动动画自动暂停，显示时恢复（省 CPU）

### Settings
- **SettingsWindow**：AI 模型配置 + 开机自启动，深色主题
- 配置保存在 `%APPDATA%\PersonalAssistant\settings.json`
- 从托盘右键菜单"设置"打开

### Tray
- **TrayService**：系统托盘图标（代码绘制蓝紫渐变 "AI" 图标）+ 右键菜单（显示主窗口、设置、插件管理、退出）
- **UserSettingsService**：管理用户级配置（API Key/Model/Endpoint/AutoStart），含注册表操作。配置文件损坏时记录警告并降级为默认值

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
- **IToolPlugin 实例（7个）**：~14个单例对象，无定时器/线程，空闲时零开销
- **PluginAggregator**：1个单例，持有 2 个 List（_allPlugins + _activePlugins），GetAllTools 带缓存（零分配直到失效），ExecuteToolStepAsync 线性扫描 O(n)，每插件独立 try-catch
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
- 插件启用状态：保存在 `%APPDATA%\PersonalAssistant\plugin_state.json`（不入库）

## 启动流程

1. `App.xaml.cs` → `Host.CreateDefaultBuilder` + Serilog + DI 注册（Scrutor 3 种扫描 + 手动注册）
2. `UserSettingsService` 从 `%APPDATA%` 加载配置
3. `MainWindow` 加载 → 注册 `Alt+Space` 全局热键 → 显示 `ChatView`
4. `TrayService` 初始化托盘图标
5. `SchedulerService` 初始化后台定时任务调度（30s 间隔）
6. 用户发送消息（普通文本或 `/clear`）→ `ChatViewModel.SendAsync()` 本地拦截 `/clear` 或调用 `ChatAgentService.SendMessageStreaming()` → MAF 工具循环 → DeepSeek API（流式输出）
7. 关闭窗口 → 隐藏主窗口 → 显示卡通人偶浮动窗
8. 最小化窗口 → 隐藏主窗口（不在任务栏） → 显示人偶
9. 点击人偶 / 托盘"显示主窗口" → 隐藏人偶 → 恢复主窗口

## 项目结构

```
PersonalAssistant/
├── App.xaml / App.xaml.cs              # 应用入口 + DI（3 种扫描 + 手动注册）
├── MainWindow.xaml / MainWindow.xaml.cs # FluentWindow + TitleBar + ChatView
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
│   │   ├── Models/                      # ChatMessage + ChatSettings + 枚举
│   │   ├── Services/
│   │   │   ├── ChatAgentService.cs      # 瘦身版 MAF 封装 (~130行) + ChatSystemPrompt.cs
│   │   │   ├── LocalModelService.cs      # 本地 LLM 推理
│   │   │   └── ModelRoutingService.cs    # 自动模型路由（本地/远程）
│   │   ├── ViewModels/ChatViewModel.cs
│   │   └── Views/ChatView.xaml/.cs
│   ├── Plugins/                         # 自包含插件模块 (7个) + 管理窗口
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
│   │   ├── PluginManagementWindow.xaml  # 插件管理窗口 (View)
│   │   └── PluginManagementWindow.xaml.cs
│   ├── Workflow/
│   │   ├── Models/                      # ToolCallRecord, WorkflowDefinition, PatternMatch
│   │   └── Services/                    # WorkflowRecorder, PatternDetector,
│   │                                    # WorkflowStorageService, WorkflowExecutorService
│   ├── Scheduler/
│   │   ├── Models/                      # ScheduledTask
│   │   └── Services/                    # SchedulerService, SchedulerStorageService
│   ├── Mascot/
│   │   ├── MascotWindow.xaml            # XAML 形状绘制的机器人（无 DropShadowEffect）
│   │   └── MascotWindow.xaml.cs         # 眼球追踪、悬停、点击弹跳、拖拽逻辑
│   └── Settings/
│       ├── SettingsWindow.xaml          # AI 配置 + 开机自启动
│       └── SettingsWindow.xaml.cs
├── Infrastructure/Common/
│   ├── Helpers/                         # BrowserDetector, StartMenuScanner, AppIconGenerator, 通用转换器
│   └── Services/                        # TrayService, UserSettingsService, ChatHistoryService, PluginStateService
├── docs/
│   ├── PLUGIN_DEV_GUIDE.md               # 外部插件开发手册
│   └── template/                         # 通用规范模板（git submodule）
├── appsettings.json                     # Serilog 日志配置 (不入库)
└── appsettings.template.json            # Serilog 模板 (入库)
```
