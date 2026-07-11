# PersonalAssistant

WPF 桌面 AI 助手——通过 DeepSeek API 提供 AI 对话+工具调用能力。带卡通机器人悬浮窗 + 系统托盘，支持开机自启动。

## 核心价值

| 价值 | 说明 |
|------|------|
| 🖥 **Windows 深度集成** | 不追求跨平台。Win32 API 操控窗口、进程、剪贴板、热键、开机自启，将系统能力用到底 |
| 🤖 **AI 自动化（能动手）** | AI 可直接读写文件、执行 Shell、操控窗口、发送按键——是 Agent 不是 Chatbot |
| 💰 **Token 消耗最小化** | 简单对话走本地模型零 token、工作流本地回放、代码逻辑优先——API 成本最低 |
| 📴 **离线可用** | 本地 Qwen 0.5B 保底，断网不中断，数据不出本机 |
| 🔌 **可扩展** | 单文件 .cs 插件热插拔，GitHub Gists 社区市场 |

## 功能

| 模块 | 说明 |
|------|------|
| AI 对话 | 流式输出、Markdown 渲染、多对话管理、消息编辑/重发、图片粘贴 |
| 工具系统 | 27+ 个 AI 工具（文件/系统/网络/搜索/天气/知识库/定时/工作流），插件化热插拔 |
| 天气插件 | 实时天气 + 6 类衣食住行智能建议（穿衣/运动/洗车/户外/饮食/健康），零 token |
| 智能剪贴板 | 内容自动分类+上下文菜单（URL/代码/JSON/颜色/文件路径/数学表达式） |
| 桌面小组件 | 天气/待办/系统状态卡片，可折叠侧边栏 |
| 定时任务 | 每日定时执行 AI 工具，代码层本地路由，零 token |
| 工作流 | 录制重复操作 → 保存 → 本地回放，零 token |
| 知识库 | TF-IDF 全文检索本地文档（.md/.txt/.pdf） |
| 本地模型 | Qwen2.5-0.5B 离线推理，断网可用 |
| 模型路由 | 3 层漏斗：本地命令拦截 → 本地小模型 → 远程 DeepSeek，token 消耗最小化 |

## 技术栈

| 用途 | 技术 |
|------|------|
| 框架 | .NET 10 + WPF + WPF-UI 4.0 |
| AI | Microsoft Agent Framework + DeepSeek API |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| DI | Scrutor 6.1 自动扫描 |
| 本地 LLM | LLamaSharp 0.27 + Qwen2.5-0.5B-Instruct GGUF |
| Markdown | MdXaml 1.27 (MIT) |
| HTML 解析 | AngleSharp 1.5 (MIT) |
| 日志 | Serilog 4.3 |
| 重试 | Polly 8.7 (BSD-3) |

## 架构

```
ChatAgentService (MAF 生命周期 + 流式输出)
    │
    ├── IToolPluginHost (PluginAggregator)
    │       ├── WeatherPlugin     ← IProactivePlugin（代码层主动触发，零 token）
    │       ├── SystemToolsPlugin  (13 tools)
    │       ├── WebToolsPlugin     (2 tools)
    │       ├── SystemInfoPlugin   (2 tools)
    │       ├── ChatToolsPlugin    (2 tools)
    │       ├── SchedulerPlugin    (3 tools)
    │       ├── WorkflowPlugin     (4 tools)
    │       ├── LocalLLMPlugin     (1 tool)
    │       └── KnowledgeBasePlugin (1 tool)
    │
    ├── MessagePreprocessor（本地拦截 → 主动插件 → 预搜索）
    └── IDangerousToolPolicy（高危工具确认）
```

## 快速开始

1. 注册 [DeepSeek API](https://platform.deepseek.com/) 获取 Key
2. 启动应用 → 托盘右键 → 设置 → 填入 API Key
3. `Alt+Space` 呼出主窗口，开始对话

## 插件开发

单个 `.cs` 文件放入 `%APPDATA%\PersonalAssistant\Plugins\` 即可扩展 AI 能力。详见 [插件开发手册](docs/PLUGIN_DEV_GUIDE.md)。

## 项目结构

详见 [CLAUDE.md](CLAUDE.md)
