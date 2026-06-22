# PersonalAssistant - Claude 项目规范

> WPF 桌面 AI 助手，通过 DeepSeek API 提供 AI 对话+工具调用能力。
> 带卡通机器人悬浮窗 + 系统托盘，支持开机自启动。
> 基于 Microsoft Agent Framework (MAF) 实现 AI 对话、工具调用循环和流式输出。

## 维护规则（强制）

**对项目的任何代码改动（新增/删除文件、修改功能、变更架构、调整配置），必须在提交前同步更新本 CLAUDE.md 文件中对应的章节。** 模板文件 `docs/template/CLAUDE_TEMPLATE.md` 也需要同步更新。

## 模板引用

本项目遵循 `docs/template/CLAUDE_TEMPLATE.md` 中的通用规范（架构、MVVM、DI、日志、安全护栏等）。
以下仅描述本项目的特有内容。

## Token 消耗最小化原则（强制）

> 每新增功能，优先以**本地程序逻辑**实现，处理不了的才请求 AI 模型，减少 token 消耗。

| 规则 | 说明 |
|------|------|
| **斜杠命令优先本地** | `/clear`、`/workflows`、`/schedules` 等命令在 ChatViewModel 中本地拦截处理，不发送 AI |
| **定时任务免 AI** | SchedulerService 直接调用 `ExecuteToolStepAsync()` 本地路由执行，零 token 消耗 |
| **工作流回放免 AI** | `/run` 命令本地遍历步骤序列，直接执行工具方法，不经过 AI |
| **新增命令先评估** | 新增斜杠命令或其他功能时，必须先评估：能否本地处理？仅当需要 AI 理解/生成能力时才调用模型 |
| **仅自然语言对话调 AI** | 只有用户输入的普通聊天消息（非斜杠命令）才发送给 DeepSeek API |

## 代码优先原则（强制）

> 能通过代码逻辑实现的功能，优先写代码，不依赖 AI 模型。AI 仅用于需要理解/生成自然语言的场景。

| 场景 | 实现方式 |
|------|------|
| 定时任务调度 | 代码逻辑：`System.Threading.Timer` + `switch` 路由 |
| 工作流录制/回放 | 代码逻辑：`List<ToolCallRecord>` + 本地遍历执行 |
| 模式检测 | 代码逻辑：环形缓冲 + 序列匹配算法 |
| 斜杠命令解析 | 代码逻辑：`string.StartsWith` + 正则匹配 |
| 工具执行 | 代码逻辑：`ExecuteToolStepAsync()` 本地 `switch` 分发 |
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
| 用户配置 | `%APPDATA%\PersonalAssistant\settings.json` (不入库，每台电脑独立) |

## 架构变化（MAF 迁移后）

```
旧：OpenAI SDK → ChatService (手动 while 循环) → ToolService → DeepSeek
新：MAF → ChatAgentService (内建工具循环) → AIFunction[] → DeepSeek
                                    └── AgentSession (内建历史持久化)
```

核心替换：
- `ChatClientAgent`（继承 `AIAgent`）替代手写 `ChatClient` + `while(true)` 工具循环
- `AIFunctionFactory.Create()` + `[Description]` 属性替代手写 `BinaryData` JSON Schema
- `AgentSession` 替代 `List<ChatMessage>` 手动管理历史
- `RunStreamingAsync()` 替代 `CompleteChatAsync()` 一次性返回，实现逐 token 流式输出

## 功能模块

### Chat
- **ChatAgentService**：封装 MAF AIAgent + 6 个工具方法 + DeepSeek 端点兼容。工具：read_file, write_file, list_files, web_fetch, run_shell (PowerShell 命令), run_command (ShellExecute 启动程序，立即返回), find_app (搜索开始菜单), send_keys (Win32 SendInput 按键组合/文本输入), window_info (焦点窗口+可见窗口列表), focus_window (按标题聚焦窗口)。工具方法内建 WorkflowRecorder 集成，每次工具调用自动录制。支持 `SendMessageStreaming()` 流式输出和 `ClearHistoryAsync()` 清空历史。资源成本：仅消息发送时消耗，空闲时零开销（事件驱动）。
- **ChatViewModel**：消息列表管理、流式 AI 响应更新、斜杠命令处理（/clear, /workflows, /run, /delete）、模式建议展示、InfoBar 错误显示。消息上限 200 条自动修剪。
- **ChatMessage**：继承 `ObservableObject`，Content 属性支持 `INotifyPropertyChanged` 供流式输出时 UI 实时更新。
- **ChatView**：WPF 深色科技风聊天界面（消息气泡、输入框、加载动画），无 DropShadowEffect

### Workflow（学习能力，Phase 2 新增）
- **WorkflowRecorder**：录制每轮对话中的工具调用序列（工具名列表）
- **PatternDetector**：最近 50 轮环形缓冲 + 序列匹配（≥3 次重复）→ 触发建议。已建议的序列不重复提示（`_shownKeys` HashSet）
- **WorkflowStorageService**：JSON 持久化到 `%APPDATA%\PersonalAssistant\workflows\` 目录
- **WorkflowExecutorService**：本地回放已保存工作流，不调用 AI，直接通过 `ChatAgentService.ExecuteToolStepAsync()` 执行

### Scheduler（定时任务，Phase 3 新增）
- **SchedulerService**：System.Threading.Timer 30s 间隔检查，SemaphoreSlim 防重入，匹配 HH:mm → 检查 LastRunDate → ExecuteToolStepAsync → 更新 LastRunDate。IDisposable 清理 Timer + Semaphore。
- **SchedulerStorageService**：JSON 持久化到 `%APPDATA%\PersonalAssistant\schedules\` 目录
- **ScheduledTask**：POCO 模型（Name, TimeOfDay, ToolName, ToolArgs, IsEnabled, CreatedAt, LastRunDate）

### 新增命令

| 命令 | 触发 | 作用 |
|------|------|------|
| `/workflows` | ChatViewModel | 列出已保存工作流 |
| `/run <name>` | ChatViewModel | 本地回放工作流 |
| `/delete <name>` | ChatViewModel | 删除工作流 |
| `/record <name>` | ChatViewModel | 开始教学模式，录制后续工具调用 |
| `/stop` | ChatViewModel | 停止录制并保存工作流 |
| `/schedule add "HH:mm" <工具> "参数"` | ChatViewModel | 创建每日定时任务 |
| `/schedules` | ChatViewModel | 列出所有定时任务 |
| `/schedule delete "name"` | ChatViewModel | 删除定时任务 |
| 建议回复 `yes 名字` | ChatViewModel | 保存检测到的重复序列为工作流 |

### Mascot
- **MascotWindow**：卡通机器人悬浮窗，纯 XAML 绘制（椭圆/矩形/Path 拼合）
- **鼠标交互：** 眼球追踪鼠标（25fps 节流）、悬停放大 1.12x + 天线变青、点击压缩弹跳、可拖动
- 关闭/最小化主窗口 → 显示悬浮窗（右下角，始终置顶，正弦浮动动画）
- 点击悬浮窗（未拖动）→ 隐藏人偶 → 恢复主窗口并聚焦输入框
- 人偶隐藏时浮动动画自动暂停，显示时恢复（省 CPU）

### Settings
- **SettingsWindow**：AI 模型配置 + 开机自启动，深色主题
- 配置保存在 `%APPDATA%\PersonalAssistant\settings.json`
- 从托盘右键菜单"设置"打开

### Tray
- **TrayService**：系统托盘图标（代码绘制蓝紫渐变 "AI" 图标）+ 右键菜单（显示主窗口、设置、退出）
- **UserSettingsService**：管理用户级配置（API Key/Model/Endpoint/AutoStart），含注册表操作

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

### 学习能力资源评估
- **WorkflowRecorder**：近似零开销（仅追加到 `List<string>`，每轮对话清空）
- **PatternDetector**：按需消耗（仅在每轮结束时做 O(n) 序列匹配，n ≤ 50）
- **WorkflowStorageService**：按需消耗（仅读写时触发磁盘 I/O）
- **WorkflowExecutorService**：按需消耗（仅 `/run` 命令时执行，不调 AI）

### 定时任务资源评估
- **SchedulerService**：30s 定时器 Tick 检查（O(1) 字符串比较），无任务时 CPU 趋近零
- **SchedulerStorageService**：按需消耗（仅 Tick 匹配时读磁盘 + 执行后写 LastRunDate）

## 配置约定

- AI 配置通过托盘 → "设置" 窗口修改，保存在用户目录 `%APPDATA%\PersonalAssistant\settings.json`
- `DEEPSEEK_API_KEY` 环境变量可作为替代，优先级高于配置文件
- `appsettings.json`（不入库）：仅含 `Serilog` 日志配置
- `appsettings.template.json`（入库）：Serilog 占位符模板
- 开机自启动：通过注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PersonalAssistant` 管理
- 工作流数据：保存在 `%APPDATA%\PersonalAssistant\workflows\` 目录
- 定时任务数据：保存在 `%APPDATA%\PersonalAssistant\schedules\` 目录

## 启动流程

1. `App.xaml.cs` → `Host.CreateDefaultBuilder` + Serilog + DI 注册（Scrutor 扫描 + 手动注册）
2. `UserSettingsService` 从 `%APPDATA%` 加载配置
3. `MainWindow` 加载 → 显示 `ChatView`
4. `TrayService` 初始化托盘图标
5. `SchedulerService` 初始化后台定时任务调度（30s 间隔）
6. 用户发送消息 → `ChatViewModel.SendAsync()` → `ChatAgentService.SendMessageStreaming()` → MAF → DeepSeek API（流式输出）
7. 关闭窗口 → 隐藏主窗口 → 显示卡通人偶浮动窗
8. 最小化窗口 → 隐藏主窗口（不在任务栏） → 显示人偶
9. 点击人偶 / 托盘"显示主窗口" → 隐藏人偶 → 恢复主窗口

## 项目结构

```
PersonalAssistant/
├── App.xaml / App.xaml.cs              # 应用入口 + DI
├── MainWindow.xaml / MainWindow.xaml.cs # FluentWindow + TitleBar + ChatView
├── Features/
│   ├── Chat/
│   │   ├── Models/                      # ChatMessage + ChatSettings + 枚举
│   │   ├── Services/                    # ChatAgentService (MAF 封装)
│   │   ├── ViewModels/ChatViewModel.cs
│   │   └── Views/ChatView.xaml/.cs
│   ├── Mascot/
│   │   ├── MascotWindow.xaml            # XAML 形状绘制的机器人（无 DropShadowEffect）
│   │   └── MascotWindow.xaml.cs         # 眼球追踪、悬停、点击弹跳、拖拽逻辑
│   ├── Settings/
│   │   ├── SettingsWindow.xaml          # AI 配置 + 开机自启动
│   │   └── SettingsWindow.xaml.cs
│   └── Workflow/
│       ├── Models/                      # ToolCallRecord, WorkflowDefinition, PatternMatch
│       └── Services/                    # WorkflowRecorder, PatternDetector,
│                                        # WorkflowStorageService, WorkflowExecutorService
│   └── Scheduler/
│       ├── Models/                      # ScheduledTask
│       └── Services/                    # SchedulerService, SchedulerStorageService
├── Infrastructure/Common/
│   ├── Helpers/                         # BrowserDetector, StartMenuScanner, AppIconGenerator, 通用转换器
│   └── Services/                        # TrayService, UserSettingsService
├── appsettings.json                     # Serilog 日志配置 (不入库)
└── appsettings.template.json            # Serilog 模板 (入库)
```
