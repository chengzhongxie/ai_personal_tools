# PersonalAssistant - Claude 项目规范

> WPF 桌面 AI 助手，通过 DeepSeek API 提供 AI 对话+工具调用能力。
> 带卡通机器人悬浮窗 + 系统托盘，支持开机自启动。

## 维护规则（强制）

**对项目的任何代码改动（新增/删除文件、修改功能、变更架构、调整配置），必须在提交前同步更新本 CLAUDE.md 文件中对应的章节。** 模板文件 `docs/template/CLAUDE_TEMPLATE.md` 也需要同步更新。

## 模板引用

本项目遵循 `docs/template/CLAUDE_TEMPLATE.md` 中的通用规范（架构、MVVM、DI、日志、安全护栏等）。
以下仅描述本项目的特有内容。

---

## 项目特有技术栈

| 用途 | 库/技术 |
|------|---------|
| AI API | DeepSeek API (via OpenAI .NET SDK) |
| 聊天界面 | WPF + 深色科技风 Chat Bubble UI |
| MVVM | CommunityToolkit.Mvvm 8.4.0 |
| DI | Scrutor 6.1.0 自动扫描 |
| UI 框架 | WPF-UI 4.0.3 (FluentWindow, InfoBar, ProgressRing) |
| 日志 | Serilog 4.3.0 |
| 用户配置 | `%APPDATA%\PersonalAssistant\settings.json` (不入库，每台电脑独立) |

## 功能模块

### Chat
- **ChatService**：懒加载 DeepSeek API 客户端，首次发送消息时才校验 Key
- **ToolService**：5 个工具实现（read_file, write_file, list_files, web_fetch, run_shell）
- **ChatViewModel**：消息列表管理、发送/清空命令、InfoBar 错误显示，消息上限 200 条自动修剪
- **ChatView**：WPF 深色科技风聊天界面（消息气泡、输入框、加载动画），无 DropShadowEffect

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
| **消息上限 200** | ChatService 和 ChatViewModel 各限 200 条，超出自动修剪最旧消息，防止内存无限增长 |
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

### 新功能开发检查清单

开发新功能时必须逐项确认：

- [ ] 是否引入了新的 WPF 视觉效果？（DropShadowEffect / BlurEffect = 禁止）
- [ ] 是否有定时器或循环逻辑？间隔是多少？
- [ ] 隐藏/不活跃时是否释放了不需要的资源？
- [ ] 所有 Brush / Animation / EasingFunction 是否已复用/冻结？
- [ ] 新建的对象是否都会在合理时间内被 GC 回收？（无 eternal 强引用）
- [ ] 内存增长是否有上界？（集合类必须有容量上限）

## 配置约定

- AI 配置通过托盘 → "设置" 窗口修改，保存在用户目录 `%APPDATA%\PersonalAssistant\settings.json`
- `DEEPSEEK_API_KEY` 环境变量可作为替代，优先级高于配置文件
- `appsettings.json`（不入库）：仅含 `Serilog` 日志配置
- `appsettings.template.json`（入库）：Serilog 占位符模板
- 开机自启动：通过注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PersonalAssistant` 管理

## 启动流程

1. `App.xaml.cs` → `Host.CreateDefaultBuilder` + Serilog + DI 注册（Scrutor 扫描 + 手动注册）
2. `UserSettingsService` 从 `%APPDATA%` 加载配置
3. `MainWindow` 加载 → 显示 `ChatView`
4. `TrayService` 初始化托盘图标
5. 用户发送消息 → `ChatViewModel.SendAsync()` → `ChatService.SendMessageAsync()` → DeepSeek API
6. 关闭窗口 → 隐藏主窗口 → 显示卡通人偶浮动窗
7. 最小化窗口 → 隐藏主窗口（不在任务栏） → 显示人偶
8. 点击人偶 / 托盘"显示主窗口" → 隐藏人偶 → 恢复主窗口

## 项目结构

```
PersonalAssistant/
├── App.xaml / App.xaml.cs              # 应用入口 + DI
├── MainWindow.xaml / MainWindow.xaml.cs # FluentWindow + TitleBar + ChatView
├── Features/
│   ├── Chat/
│   │   ├── Models/                      # 消息/响应/设置模型 + 枚举
│   │   ├── Services/                    # ChatService, ToolService
│   │   ├── ViewModels/ChatViewModel.cs
│   │   └── Views/ChatView.xaml/.cs
│   ├── Mascot/
│   │   ├── MascotWindow.xaml            # XAML 形状绘制的机器人（无 DropShadowEffect）
│   │   └── MascotWindow.xaml.cs         # 眼球追踪、悬停、点击弹跳、拖拽逻辑
│   └── Settings/
│       ├── SettingsWindow.xaml          # AI 配置 + 开机自启动
│       └── SettingsWindow.xaml.cs
├── Infrastructure/Common/
│   ├── Helpers/                         # 通用转换器 (BoolToVisibilityConverter 等)
│   └── Services/                        # TrayService, UserSettingsService
├── appsettings.json                     # Serilog 日志配置 (不入库)
└── appsettings.template.json            # Serilog 模板 (入库)
```
