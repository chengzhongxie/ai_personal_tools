# PersonalAssistant - Claude 项目规范

> WPF 桌面 AI 助手，通过 DeepSeek API 提供 AI 对话+工具调用能力。

## 模板引用

本项目遵循 `docs/template/CLAUDE_TEMPLATE.md` 中的通用规范（架构、MVVM、DI、日志、安全护栏等）。
以下仅描述本项目的特有内容。

---

## 项目特有技术栈

| 用途 | 库/技术 |
|------|---------|
| AI API | DeepSeek API (via OpenAI .NET SDK) |
| 聊天界面 | WPF + Custom Chat Bubble UI |
| MVVM | CommunityToolkit.Mvvm 8.4.0 |
| DI | Scrutor 6.1.0 自动扫描 |
| UI 框架 | WPF-UI 4.0.3 (FluentWindow, InfoBar, ProgressRing) |
| 日志 | Serilog 4.3.0 |
| 配置 | ChatSettings (DeepSeek API Key/Model/Endpoint) |

## 功能模块

### Chat
- **ChatService**：封装 DeepSeek API 调用 + tool-call 循环
- **ToolService**：5 个工具实现（read_file, write_file, list_files, web_fetch, run_shell）
- **ChatViewModel**：消息列表管理、发送/清空命令、InfoBar 错误显示
- **ChatView**：WPF 聊天界面（消息气泡、输入框、加载动画）

## 配置约定

- `appsettings.json`（不入库）：包含 `ChatSettings` 和 `Serilog` 配置
- `appsettings.template.json`（入库）：占位符模板
- API Key 可在环境变量 `DEEPSEEK_API_KEY` 中设置，优先级高于配置文件

## 启动流程

1. `App.xaml.cs` → `Host.CreateDefaultBuilder` + Serilog + DI 注册（Scrutor 扫描）
2. `MainWindow` 加载 → 显示 `ChatView`
3. 用户发送消息 → `ChatViewModel.SendAsync()` → `ChatService.SendMessageAsync()` → DeepSeek API

## 项目结构

```
PersonalAssistant/
├── App.xaml / App.xaml.cs              # 应用入口 + DI
├── MainWindow.xaml / MainWindow.xaml.cs # FluentWindow 壳
├── Features/Chat/
│   ├── Models/                          # 消息/响应/设置模型 + 枚举
│   ├── Services/                        # ChatService, ToolService
│   ├── ViewModels/ChatViewModel.cs
│   └── Views/ChatView.xaml/.cs
├── Infrastructure/Common/Helpers/       # 通用转换器
└── Converters/                          # 消息对齐转换器
```
