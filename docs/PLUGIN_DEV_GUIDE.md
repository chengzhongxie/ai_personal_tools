# PersonalAssistant 插件开发手册

---

## 目录

- [1. 概述](#1-概述)
- [2. 快速入门](#2-快速入门)
- [3. PluginBase API 参考](#3-pluginbase-api-参考)
- [4. 工具定义详解](#4-工具定义详解)
- [5. 参数处理](#5-参数处理)
- [6. 提示词片段（可选）](#6-提示词片段可选)
- [7. 安装与部署](#7-安装与部署)
- [8. 插件管理](#8-插件管理)
- [9. 可用 API 范围](#9-可用-api-范围)
- [10. 完整示例](#10-完整示例)
- [11. 最佳实践](#11-最佳实践)
- [12. 常见陷阱与设计决策](#12-常见陷阱与设计决策)
- [13. 排查问题](#13-排查问题)

---

## 1. 概述

PersonalAssistant 的外部插件系统允许你通过**单个 `.cs` 文件**扩展 AI 助手的能力。插件的工具方法会自动注册到 AI 模型的工具列表中，用户用自然语言即可触发。

### 核心特性

| 特性 | 说明 |
|------|------|
| **零编译** | 只需编写一个 `.cs` 文件，应用启动时 Roslyn 自动编译 |
| **热插拔** | 放入/删除文件后重启应用即生效 |
| **无外部依赖** | 插件代码只需引用 `System` / `System.Threading.Tasks` / `System.Text.Json` |
| **AI 自动发现** | 插件工具自动注册到 AI 工具列表，用户用自然语言即可调用 |
| **优先级控制** | 外部插件可覆盖同名内置工具 |
| **可视化管理** | 托盘菜单 → 插件管理 → 查看/启用/禁用/删除/导入 |

### 架构简图

```
你的 .cs 文件
    │
    ▼
PluginLoader (Roslyn 编译)
    │
    ▼
PluginBase 实例
    │
    ▼
ExternalPluginAdapter (包装为 IToolPlugin)
    │
    ▼
PluginAggregator (注册到 AI 工具列表)
    │
    ▼
AI 模型可调用你的工具
```

---

## 2. 快速入门

### 最小的插件（无参数工具）

创建一个文件 `HelloPlugin.cs`：

```csharp
using System.Threading.Tasks;
using PersonalAssistant.Core.Plugins;

public class HelloPlugin : PluginBase
{
    public override string Name => "HelloPlugin";
    public override string Description => "一个打招呼的示例插件";

    public override PluginToolDefinition[] GetToolDefinitions()
    {
        return new[]
        {
            new PluginToolDefinition
            {
                Name = "say_hello",
                Description = "向用户打招呼，返回一条问候语"
                // Parameters 为 null = 无参数
            }
        };
    }

    public override async Task<string?> ExecuteToolAsync(string toolName, string args)
    {
        if (toolName == "say_hello")
        {
            return "你好！我是你的 AI 助手，有什么可以帮你的？";
        }
        return null; // 工具名不匹配
    }
}
```

### 带参数的插件

```csharp
using System;
using System.Text.Json;
using System.Threading.Tasks;
using PersonalAssistant.Core.Plugins;

public class CalculatorPlugin : PluginBase
{
    public override string Name => "Calculator";
    public override string Description => "简单计算器插件";

    public override PluginToolDefinition[] GetToolDefinitions()
    {
        return new[]
        {
            new PluginToolDefinition
            {
                Name = "calculate",
                Description = "执行简单数学计算",
                Parameters = new[]
                {
                    new PluginParameter
                    {
                        Name = "expression",
                        Description = "数学表达式，如 '2+3*4'",
                        Type = "string",
                        Required = true
                    }
                }
            }
        };
    }

    public override Task<string?> ExecuteToolAsync(string toolName, string args)
    {
        if (toolName != "calculate")
            return Task.FromResult<string?>(null);

        try
        {
            // 解析 JSON 参数
            using var doc = JsonDocument.Parse(args);
            var expression = doc.RootElement.GetProperty("expression").GetString();

            // 简单计算（仅演示，生产环境应更健壮）
            var result = new System.Data.DataTable().Compute(expression, null);
            return Task.FromResult<string?>($"计算结果: {expression} = {result}");
        }
        catch (Exception ex)
        {
            return Task.FromResult<string?>($"计算失败: {ex.Message}");
        }
    }
}
```

---

## 3. PluginBase API 参考

`PluginBase` 是插件作者**唯一需要继承**的抽象类，定义在 `PersonalAssistant.Core.Plugins` 命名空间。

### 必须重写的成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `Name` | `abstract string` | 插件名称，用于日志和调试。建议使用 PascalCase |
| `Description` | `abstract string` | 插件用途和功能说明，显示在插件管理界面中供用户了解插件功能 |
| `GetToolDefinitions()` | `abstract PluginToolDefinition[]` | 返回此插件提供的所有工具元数据 |
| `ExecuteToolAsync(string toolName, string args)` | `abstract Task<string?>` | 执行指定工具并返回结果 |

### 可选重写的成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `GetPromptFragment()` | `virtual string?` | 返回提示词片段，注入 AI 系统提示词。默认返回 `null` |

### 自动设置的成员（由 PluginLoader 设置）

| 成员 | 类型 | 说明 |
|------|------|------|
| `SourceFilePath` | `string?` | 插件源文件完整路径。内置插件为 `null` |

---

## 4. 工具定义详解

### PluginToolDefinition

```csharp
public sealed class PluginToolDefinition
{
    public string Name { get; init; }              // 工具名，AI 通过此名称调用
    public string Description { get; init; }        // AI 看到的工具描述，越详细越准确
    public IReadOnlyList<PluginParameter>? Parameters { get; init; } // null = 无参数
}
```

### PluginParameter

```csharp
public sealed class PluginParameter
{
    public string Name { get; init; }        // 参数名
    public string Description { get; init; }  // 参数说明
    public string Type { get; init; } = "string";  // "string" | "number" | "boolean"
    public bool Required { get; init; } = true;    // 是否必填
}
```

### 工具命名规范

- 使用 `snake_case` 命名：`search_files`, `translate_text`, `send_email`
- 名称应直观表达功能，AI 会根据名称和描述决定何时调用
- 外部插件工具名与内置工具冲突时，外部插件优先（并记录 warning 日志）

### Description 编写技巧

`Description` 是 AI 决定是否调用你的工具的关键依据：

```csharp
// ❌ 太模糊
Description = "Search something"

// ✅ 清晰描述功能和适用场景
Description = "搜索本地文件系统，支持通配符模式匹配（如 *.txt, report-*.pdf）。" +
              "返回匹配的文件路径列表。用于查找用户指定的文件。"
```

---

## 5. 参数处理

### 参数格式

所有工具参数以 **JSON 字符串** 形式传入 `ExecuteToolAsync` 的 `args` 参数：

```json
{"param1": "value1", "param2": 42, "param3": true}
```

### 解析参数

使用 `System.Text.Json`（已在编译环境中可用）：

```csharp
public override async Task<string?> ExecuteToolAsync(string toolName, string args)
{
    using var doc = JsonDocument.Parse(args);
    var root = doc.RootElement;

    // 获取可选参数（安全访问）
    var name = root.TryGetProperty("name", out var n) ? n.GetString() : "默认值";

    // 获取必填参数
    var url = root.GetProperty("url").GetString();

    // 获取数值参数
    var count = root.TryGetProperty("count", out var c) && c.TryGetInt32(out var ci) ? ci : 10;

    // 获取布尔参数
    var verbose = root.TryGetProperty("verbose", out var v) && v.GetBoolean();
}
```

### 无参数工具

如果工具没有参数，`args` 会传入 `"{}"`（空 JSON 对象）。直接忽略即可：

```csharp
if (toolName == "my_no_args_tool")
{
    return "直接返回结果，无需解析参数";
}
```

---

## 6. 提示词片段（可选）

重写 `GetPromptFragment()` 可以向 AI 的系统提示词中注入额外说明。AI 会在**每次对话**中看到这些信息。

```csharp
public override string? GetPromptFragment()
{
    return """
        汇率转换插件已启用。
        - 支持货币: CNY, USD, EUR, JPY, GBP
        - 汇率数据每日更新
        - convert_currency 工具接受 from/to/amount 三个参数
        """;
}
```

**使用时机：**
- 工具需要特定的使用上下文（如数据来源、限制条件）
- 需要告诉 AI 多个工具之间的协作关系
- 需要说明插件特有的术语或概念

**不要滥用：** 提示词片段会增加每次对话的 token 消耗。简单的工具无需添加片段，`Description` 已足够。

---

## 7. 安装与部署

### 安装路径

```
%APPDATA%\PersonalAssistant\Plugins\
```

通常展开为：`C:\Users\<用户名>\AppData\Roaming\PersonalAssistant\Plugins\`

### 安装方式

#### 方式一：手动复制

将 `.cs` 文件复制到上述目录，重启应用。

#### 方式二：通过管理窗口导入

1. 右键托盘图标 → **插件管理**
2. 点击 **导入插件**
3. 选择一个或多个 `.cs` 文件
4. 窗口会即时编译验证，成功/失败都会提示

### 启用/禁用

- 托盘 → 插件管理 → 勾选/取消勾选启用复选框
- 变更后**需重启应用生效**

### 删除

- 外部插件：管理窗口中点击 **删除** 按钮（会删除源文件）
- 内置插件：不可删除，只能禁用

### 生效时机

- **新增/删除**：重启应用后生效
- **启用/禁用**：重启应用后生效
- 修改已加载的 `.cs` 文件：重启应用后生效（重新编译）

---

## 8. 插件管理

### 打开管理窗口

系统托盘右键 → **插件管理**

### 窗口功能

| 功能 | 操作 | 说明 |
|------|------|------|
| 查看插件列表 | 自动展示 | 显示所有内置 + 外部插件，含用途说明和工具列表 |
| 启用/禁用 | 勾选 CheckBox | 禁用后 AI 不可调用该插件的工具 |
| 删除外部插件 | 点击"删除"按钮 | 确认后删除源文件 |
| 导入插件 | 点击"导入插件" | 选择 .cs 文件复制到 Plugins 目录 |
| 编译验证 | 导入时自动 | 编译失败会警告但文件保留 |

### 状态持久化

插件启用/禁用状态保存在 `%APPDATA%\PersonalAssistant\plugin_state.json`，格式：

```json
{
  "disabled_plugins": ["MyOldPlugin"]
}
```

---

## 9. 可用 API 范围

插件代码在 Roslyn 沙箱中编译，**仅限使用程序集引用范围内的 API**。

### 保证可用的命名空间

| 命名空间 | 说明 |
|----------|------|
| `System` | 基础类型（string, int, DateTime, Math 等） |
| `System.IO` | 文件/目录操作 |
| `System.Text.Json` | JSON 解析（参数处理） |
| `System.Threading.Tasks` | Task / async-await |
| `System.Net.Http` | HTTP 请求（需手动 `using`） |
| `System.Linq` | LINQ 查询 |
| `System.Collections.Generic` | List, Dictionary 等 |
| `System.ComponentModel` | DescriptionAttribute 等 |

### 必须引用的命名空间

```csharp
using PersonalAssistant.Core.Plugins;  // PluginBase, PluginToolDefinition, PluginParameter
```

### 可用程序集

编译环境自动包含：
- `System.Private.CoreLib` — 核心运行时
- `System.Text.Json` — JSON 处理
- `System.Threading.Tasks` — 异步支持
- `System.Linq` — LINQ
- `System.ComponentModel.Primitives` — 特性支持
- `PersonalAssistant.exe` — 含 `PluginBase` 定义
- 当前 AppDomain 中所有已加载的非动态程序集

### 限制

- **不能引用 NuGet 包**（Roslyn 编译环境无 NuGet 还原）
- **不能引用第三方 DLL**（除非主项目已引用并加载到 AppDomain）
- **不能使用 WPF/Windows Forms UI API**（无相关程序集引用）

---

## 10. 完整示例

### 天气查询插件（演示 HTTP 请求 + 参数处理）

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using PersonalAssistant.Core.Plugins;

public class WeatherPlugin : PluginBase
{
    private static readonly HttpClient _http = new();

    // ──── 插件元数据 ────
    public override string Name => "WeatherPlugin";
    public override string Description => "天气查询插件，支持中国城市天气";

    // ──── 工具声明 ────
    public override PluginToolDefinition[] GetToolDefinitions()
    {
        return new[]
        {
            new PluginToolDefinition
            {
                Name = "get_weather",
                Description =
                    "查询指定城市的天气信息。\n" +
                    "返回当前温度、湿度、天气状况和风力。\n" +
                    "适用于用户询问天气相关的任何问题。",
                Parameters = new[]
                {
                    new PluginParameter
                    {
                        Name = "city",
                        Description = "城市名称，如 '北京'、'上海'",
                        Type = "string",
                        Required = true
                    },
                    new PluginParameter
                    {
                        Name = "include_forecast",
                        Description = "是否包含未来天气预报",
                        Type = "boolean",
                        Required = false
                    }
                }
            }
        };
    }

    // ──── 工具执行 ────
    public override async Task<string?> ExecuteToolAsync(string toolName, string args)
    {
        if (toolName != "get_weather")
            return null;

        try
        {
            // 解析参数
            using var doc = JsonDocument.Parse(args);
            var root = doc.RootElement;

            var city = root.GetProperty("city").GetString()!;
            var includeForecast = root.TryGetProperty("include_forecast", out var f) &&
                                  f.GetBoolean();

            // 调用天气 API（示例使用 wttr.in）
            var url = $"https://wttr.in/{Uri.EscapeDataString(city)}?format=j1";
            var response = await _http.GetStringAsync(url);
            var weather = JsonDocument.Parse(response).RootElement;

            var current = weather.GetProperty("current_condition")[0];
            var temp = current.GetProperty("temp_C").GetString();
            var humidity = current.GetProperty("humidity").GetString();
            var desc = current.GetProperty("weatherDesc")[0]
                .GetProperty("value").GetString();
            var wind = current.GetProperty("windspeedKmph").GetString();

            var result = $"【{city} 当前天气】\n" +
                         $"温度: {temp}°C\n" +
                         $"湿度: {humidity}%\n" +
                         $"天气: {desc}\n" +
                         $"风速: {wind} km/h";

            if (includeForecast)
            {
                var forecast = weather.GetProperty("weather")[0];
                var maxTemp = forecast.GetProperty("maxtempC").GetString();
                var minTemp = forecast.GetProperty("mintempC").GetString();
                result += $"\n\n【明日预报】\n最高: {maxTemp}°C / 最低: {minTemp}°C";
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"天气查询失败: {ex.Message}";
        }
    }

    // ──── 提示词片段（可选） ────
    public override string? GetPromptFragment()
    {
        return """
            天气查询插件 (WeatherPlugin) 已启用。
            - get_weather: 查询城市天气，支持中国城市名和英文城市名
            - 数据来源: wttr.in
            """;
    }
}
```

将此文件保存为 `WeatherPlugin.cs`，放入 Plugins 目录，重启后即可使用。

---

## 11. 最佳实践

### 错误处理

```csharp
// ✅ 捕获异常并返回友好消息（不要重新抛出）
public override async Task<string?> ExecuteToolAsync(string toolName, string args)
{
    try
    {
        // 业务逻辑...
    }
    catch (Exception ex)
    {
        return $"操作失败: {ex.Message}";
        // 不要 throw — 异常会被 ExternalPluginAdapter 捕获并记录日志，
        // 但返回友好消息给 AI 能让对话继续
    }
}
```

### 异步 IO

```csharp
// ✅ 使用异步方法，不阻塞线程
public override async Task<string?> ExecuteToolAsync(...)
{
    var data = await File.ReadAllTextAsync(path);
    return data;
}

// ✅ 同步方法也包装为 Task
public override Task<string?> ExecuteToolAsync(...)
{
    var result = DoSyncWork();
    return Task.FromResult(result);
}
```

### 插件描述

`PluginBase.Description` 会在**插件管理界面**中展示给用户，应简洁清晰地说明插件的用途：

```csharp
// ✅ 简洁清晰，用户一看就懂
public override string Description => "提供 3 个定时任务工具：创建/查看/删除每日定时任务，到时间自动执行指定 AI 工具";

// ❌ 太技术化，用户看不懂
public override string Description => "基于 System.Threading.Timer 实现的定时任务调度器";
```

### 工具描述

```csharp
// ✅ 详细描述，帮助 AI 正确调用
Description = "读取文本文件内容。支持 .txt / .md / .json / .xml 等文本格式。" +
              "返回文件全部内容。仅在用户明确要求读取文件时调用。"

// ❌ 太简单，AI 可能误用
Description = "Read a file"
```

### 返回格式与 Markdown 样式

工具的返回值会通过 Markdig.Wpf 渲染为 WPF 富文本。使用 Markdown 语法可以让输出有层次、有颜色。

**支持的语法与渲染效果：**

| 语法 | 用途 | 深色主题效果 |
|------|------|-------------|
| `## 标题` | 章节标题 | 浅蓝色、22px 加粗 |
| `### 子标题` | 子章节 | 浅紫色、17px 半粗 |
| `**粗体**` | 强调关键数据 | 亮白色加粗 |
| `> 引用` | 提示/注意块 | 灰底 + 蓝左边线 |
| `` `代码` `` | 文件名/命令 | 绿字暗底 Consolas 等宽 |
| `- 列表` | 逐项列举 | 灰色正文 + 缩进 |
| `\| 列1 \| 列2 \|` | 表格 | 灰框表格 |
| `---` | 分隔线 | 灰色横线 |
| 空行 | 段落分隔 | 产生段落间距 |

**设计原则：**
- 用 `##` 分层，用 `**粗体**` 标关键值，用 `-` 列细节
- 不要返回 JSON 原始数据——用户看不懂
- 不要在工具返回值中自编颜色/字体——颜色由主题统一控制

```csharp
// ✅ Markdown 分层输出
return """
    ## 搜索结果
    找到 **3 个** 匹配文件：

    - `docs/readme.md` — 项目说明文档
    - `src/main.cs` — 主入口文件
    - `config/app.json` — 应用配置

    > 💡 如需查看文件内容，请说"读取 xxx 文件"
    """;

// ✅ 简单结果
return $"找到 **{files.Length}** 个匹配文件";

// ❌ 纯文本挤在一起
return $"找到{files.Length}个文件: {string.Join(",", files)}";

// ❌ 返回 JSON
return JsonSerializer.Serialize(files);
```

### 单文件多工具

一个插件可以提供多个相关工具：

```csharp
public override PluginToolDefinition[] GetToolDefinitions()
{
    return new[]
    {
        new PluginToolDefinition { Name = "notes_create", Description = "创建新笔记" },
        new PluginToolDefinition { Name = "notes_list", Description = "列出所有笔记" },
        new PluginToolDefinition { Name = "notes_delete", Description = "删除笔记" },
    };
}
```

### 资源管理

- 插件实例为**单例**（启动时创建，应用退出时销毁）
- 文件句柄、网络连接等资源应在使用后立即释放
- 不要在插件中启动后台线程或定时器（不会被清理）

---

## 12. 常见陷阱与设计决策

> 以下是从实际插件开发中总结的经验。在设计插件之前，务必阅读本节。

### 陷阱 1：AI 不一定会调用你的工具

**问题**：你把工具定义得很清楚，系统提示词也写好了，但 AI 就是不调用。这不是你的代码有 bug——部分 AI 模型（如 DeepSeek）的工具调用不够可靠。AI 可能直接生成回复、拒绝回答、或说"请稍等"后什么都不做。

**解决方案**：
- 如果工具对可靠性要求高，将插件贡献到主项目的 `Features/Plugins/` 目录，实现 `IToolPlugin + IProactivePlugin`（主动插件）。系统会在代码层检测用户意图并直接触发，不依赖 AI 决策。
- 外部插件（PluginBase）目前只能依赖 AI 调用。优化 `Description` 的写法可以提高调用概率，但无法保证 100%。

### 陷阱 2：外部插件与内置插件的架构差异

| | 外部插件 (PluginBase) | 内置插件 (IToolPlugin) |
|---|---|---|
| 文件 | 1 个 .cs | 1-2 个 .cs |
| 安装 | 放入 Plugins 目录 | 放入项目 Features/Plugins/ |
| AI 工具调用 | ✅ 支持 | ✅ 支持 |
| 主动触发 (IProactivePlugin) | ❌ 不支持 | ✅ 支持 |
| 绕过 AI 直接输出 (BypassAI) | ❌ 不支持 | ✅ 支持 |
| 适用场景 | 简单工具、非关键功能 | 高频查询、需要绕过 AI 安全过滤 |

**决策建议**：如果你的工具经常需要被用户主动触发（天气、新闻、股价等查询类），做成内置 `IProactivePlugin`。如果是辅助类工具（格式化、计算、转换等），外部插件足够。

### 陷阱 3：BypassAI — 什么时候跳过 AI

**问题**：插件返回了完整的中文报告，但 AI 拿到后"精简"了一遍——丢了数据、改了措辞、漏了建议。

**解决方案**：`ProactiveResult` 提供 `BypassAI` 参数：

```csharp
// BypassAI: true — 输出已是完整自然语言，直接展示给用户，零 token
return new ProactiveResult(report, BypassAI: true);

// BypassAI: false — 原始数据需要 AI 理解/格式化/个性化
return new ProactiveResult(rawData, BypassAI: false);
```

**判断标准**：你的输出能不能直接给用户看？
- 能（如：天气报告、系统状态、任务列表）→ `BypassAI: true`
- 不能（如：搜索结果摘要、需要个性化建议）→ `BypassAI: false`

记住项目的核心原则：**能用代码就不用 AI**。BypassAI 就是这个原则在插件层的体现。

### 陷阱 4：用户输入不完整 — 缺少关键参数

**问题**：用户说"明天天气怎么样"，没指定城市。你的插件需要知道查哪里。

**处理策略**：
1. **有默认值**（如城市→IP 自动定位、数量→10）→ 使用默认值，但要**明确告知用户**你用了什么默认值
2. **无默认值**（如文件名、金额）→ 返回清晰的提示信息，告诉用户需要提供什么
3. **示例对比**：

```
// ❌ 用户不知道数据是哪来的
返回: "明天 27°C ~ 32°C"

// ✅ 用户知道为什么是这个城市，怎么改
返回: "📍 深圳 天气报告
      > 🏠 未指定城市，已根据网络IP自动定位至 深圳
      > 如需查其他城市，请说 XX天气（如 北京天气）"
```

### 陷阱 5：AI 安全过滤拦截查询

**问题**：部分 AI 模型（如 DeepSeek）有内容安全过滤。用户问"明天天气"，AI 可能认为"提供未验证的实时信息"并拒绝回答。

**解决方案**：这是内置 `IProactivePlugin` 的另一个优势——数据在代码层预获取，不经过 AI 的安全过滤。外部插件无法解决此问题。

### 陷阱 6：输出格式 — 用户看到什么

**考虑因素**：
- 聊天界面通过 Markdig.Wpf 渲染 **Markdown**，不支持 HTML/CSS
- 表格 `| | |`、标题 `##`、粗体 `**`、引用 `>`、列表 `-` 都可用
- 避免纯文本堆砌——用标题分层、用列表分项、用粗体标重点
- 移动端可能不渲染表格，关键信息不要只放在表格里

**输出设计清单**：
- [ ] 核心信息是否一眼可见？（温度、天气，而非埋在段落中）
- [ ] 数据来源是否明确？（数据来自哪里、何时获取）
- [ ] 局限性是否告知？（自动定位的城市、可能过时的缓存）
- [ ] 下一步操作是否提示？（怎么查其他城市、怎么获取更详细信息）

---

## 13. 排查问题

### 插件未加载

1. 检查文件是否在 `%APPDATA%\PersonalAssistant\Plugins\` 目录下
2. 检查文件扩展名是否为 `.cs`
3. 查看日志 `%APPDATA%\PersonalAssistant\logs\app-.log`，搜索 `PluginLoader` 相关条目

### 编译错误

```log
[WARNING] [PluginLoader] 跳过编译失败的插件文件: MyPlugin.cs
```

常见原因：
- 缺少 `using PersonalAssistant.Core.Plugins;`
- 类没有继承 `PluginBase`
- 类没有使用 `public` 修饰符
- 缺少必要的方法重写
- 引用了不可用的命名空间或 NuGet 包

### 工具未被 AI 调用

1. 检查 `GetToolDefinitions()` 中的 `Description` 是否足够详细
2. 确认插件在管理窗口中处于**启用**状态
3. 确认重启应用后修改生效
4. 检查日志中是否有工具名冲突（外部插件覆盖内置工具会有 warning）

**补充说明：AI 工具调用的不确定性。** 部分 AI 模型（如 DeepSeek）的工具调用能力不够可靠——即使系统提示词明确指引、工具描述清晰，AI 也可能选择不调用工具，而是直接生成回复或拒答。这是 AI 模型本身的限制，不是插件代码的问题。

**解决方案：`IProactivePlugin` 主动触发。** 对于对可靠性要求高、或被 AI 安全过滤拦截的查询（天气、新闻、股价等），可以将其实现为内置的 `IToolPlugin + IProactivePlugin`。主动插件在代码层检测用户意图并直接触发，不依赖 AI 的调用决策。外部插件如需此能力，可以将代码贡献到主项目的 `Features/Plugins/` 目录下。

### 参数解析失败

```csharp
// JSON 解析异常通常是因为参数名不匹配
// 检查 GetToolDefinitions 中的 Parameter.Name 是否和 JSON key 一致
```

### 查看日志

日志文件位于 `%APPDATA%\PersonalAssistant\logs\`：
- `app-YYYYMMDD.log` — 业务日志（含插件加载信息）
- `errors-YYYYMMDD.log` — 错误日志（含插件执行异常）

---

## 附录：DTO 速查表

### PluginBase 继承清单

```csharp
public class MyPlugin : PluginBase
{
    public override string Name => "";                          // 必须
    public override string Description => "";                  // 必须
    public override PluginToolDefinition[] GetToolDefinitions() // 必须
        => Array.Empty<PluginToolDefinition>();
    public override Task<string?> ExecuteToolAsync(             // 必须
        string toolName, string args) => Task.FromResult(null as string?);
    public override string? GetPromptFragment() => null;       // 可选
    // SourceFilePath — 自动设置，不要手动修改
}
```

### 参数类型对照

| Type 声明 | JSON 格式 | C# 解析 |
|-----------|-----------|---------|
| `"string"` | `"hello"` | `.GetString()` |
| `"number"` | `42` | `.GetInt32()` / `.GetDouble()` |
| `"boolean"` | `true` | `.GetBoolean()` |
