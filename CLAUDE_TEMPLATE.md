# 可复用项目规范模板

> 从生产项目中提取的通用开发规范，适用于 **.NET + WPF + MVVM** 技术栈的桌面应用。
> 复制到新项目后，替换 `{ProjectName}` 等占位符即可使用。
>
> **维护规则：修改框架结构、添加新规范、变更技术栈后，必须同步更新本文件。项目特有内容（功能模块、业务模型、授权机制）应留在项目自身的 CLAUDE.md 中。**

---

## 0. AI Agent 执行安全护栏（强制）

> **以下规则约束 AI 编码助手的行为边界，防止误删数据、破坏仓库、引入漏洞或擅自扩大改动范围。**

### 0.1 数据库安全

| 规则 | 说明 |
|------|------|
| **禁止 DROP / TRUNCATE** | 任何情况下不得删除表或清空数据，除非用户明确写出完整 SQL 并要求执行 |
| **DELETE 必须有 WHERE** | 禁止无条件的 `DELETE FROM table`，WHERE 条件必须命中索引列 |
| **ALTER TABLE 需确认** | 修改表结构前必须告知用户影响范围（是否锁表、是否丢失数据），获得确认后执行 |
| **禁止操作生产库** | 无法判断是否为生产环境时，必须先询问。连接字符串含 `prod`/`production` 关键字时默认只读 |
| **事务包裹写入** | 多步写入操作必须包裹在事务内，异常时回滚 |
| **先查后改** | 修改/删除数据前，先用相同 WHERE 条件的 SELECT 确认影响行数 |

### 0.2 Git 安全

| 规则 | 说明 |
|------|------|
| **禁止 force push 到 main/master** | 即使合并冲突也不得 `--force` 推送到主分支 |
| **禁止 `git reset --hard`** | 除非用户明确要求，否则不得执行硬重置 |
| **禁止跳过 hooks** | 不得使用 `--no-verify`、`--no-gpg-sign` 等跳过检查的参数 |
| **禁止 `git add -A`** | 使用具体文件路径暂存，避免误提交 .env、密钥、二进制文件 |
| **新提交而非 amend** | 默认创建新 commit，除非用户明确要求 amend |
| **推送前确认** | 对外可见的操作（push、PR、issue、评论）需用户确认 |

### 0.3 代码修改边界

| 规则 | 说明 |
|------|------|
| **修改前必须先读** | 未读过文件内容就进行修改，极易引入不一致和 bug |
| **最小改动原则** | 只改和当前任务直接相关的内容，不顺手重构、不加"顺便优化" |
| **禁止删除不理解的代码** | 看到奇怪的写法、未使用的变量、看似冗余的逻辑 → 先问，不要直接删 |
| **禁止添加未要求的特性** | 不要加配置项、开关、错误处理、日志等"以后可能有用"的东西 |
| **保持现有风格** | 缩进、命名、注释风格跟随文件现有惯例，不强制统一 |
| **禁止修改第三方库源码** | NuGet/node_modules 中的代码只读，问题通过配置或替换库解决 |

### 0.4 安全编码

| 规则 | 说明 |
|------|------|
| **禁止硬编码密钥** | 连接字符串、API Key、Token 等必须通过环境变量或配置文件注入（且配置文件不入库） |
| **禁止命令注入** | 拼接 shell 命令时，参数必须通过参数化方式传递，不得字符串拼接用户输入 |
| **禁止 SQL 注入** | 必须使用参数化查询，禁止拼接用户输入构造 SQL |
| **禁止 XSS** | 用户输入渲染到 UI 前必须转义 |
| **文件操作限制** | 文件路径必须规范化验证，禁止路径穿越（`../` 访问上级目录） |

### 0.5 测试与验证

| 规则 | 说明 |
|------|------|
| **修改后必须跑测试** | 改动完成必须运行相关测试用例，失败则修复后再提交 |
| **不可跳过测试提交** | 测试未通过不得 commit |
| **新功能必须有测试** | 新增代码同时提交对应测试，覆盖率不低于已有水平 |
| **不假设外部依赖可用** | 测试用例不应依赖外部服务（数据库、网络、文件系统），使用 mock/fixture |

### 0.6 通用行为准则

| 规则 | 说明 |
|------|------|
| **不确定时先问** | 需求模糊、多种方案可选、影响范围不明 → 向用户确认后再动手 |
| **不猜测路径和 URL** | 文件路径、API 地址、包名等必须从实际代码中确认，不得自行编造 |
| **出错后分析根因** | 同一操作失败 3 次必须切换思路，不得重复相同调用 |
| **承认不清楚** | 遇到不熟悉的框架、库、平台特性时坦诚说明，不编造 API 或配置 |
| **保持简洁** | 回复聚焦关键信息，不添加无意义的客套话和冗余解释 |

---

## 1. 架构与设计原则

### 1.1 轻量 DDD / 特性文件夹结构

```
{ProjectName}/
├── Features/                    # 按功能模块组织
│   └── {FeatureName}/
│       ├── Models/              # 实体、DTO、枚举
│       │   ├── Entities/        # 数据库表映射实体
│       │   ├── DTOs/            # 非持久化数据传输对象
│       │   └── Enums/           # 模块私有枚举
│       ├── Services/            # 业务逻辑 + 接口（接口与实现放一起）
│       ├── ViewModels/          # MVVM ViewModel
│       └── Views/               # WPF Page/Window/UserControl
└── Infrastructure/
    ├── Database/                # 数据库抽象层
    │   ├── Core/                # 连接工厂、会话、Schema
    │   ├── Providers/           # 数据库提供者插件
    │   ├── Models/              # 数据库配置模型
    │   └── Enums/               # 数据库相关枚举
    └── Common/                  # 通用基础设施
        ├── Enums/               # 通用枚举
        ├── Helpers/             # 转换器、工具类
        ├── Models/              # 通用模型
        └── Services/            # 通用服务（Dialog 等）
```

### 1.2 类的单一职责原则（强制）

> **一个类只做一件事**。禁止将不相关的功能堆进同一个类，导致类膨胀为"上帝对象"。

- **接口隔离**：每个服务对应一个小接口，ViewModel 只注入自己需要的接口，不注入无关方法
- **类行数红线**：单个 `*Service` 类超过 **~800 行**时必须拆分。拆分依据：按业务边界而非技术层次
- **服务间解耦**：服务间通过接口互相注入，不直接 `new` 或引用具体实现。Scrutor 自动扫描 `AsImplementedInterfaces`，无需手动注册
- **私有辅助方法**：仅在所属类内部使用的 helper 留在本类；被其他类复用的逻辑必须提升为接口的 `public` 方法

### 1.3 DI 注册模式（Scrutor 自动扫描）

1. **`*Service` 实现类** → Scrutor 扫描，`AsImplementedInterfaces`，Singleton
2. **`*ViewModel` / `*Page` 类** → Scrutor 扫描，`AsSelf`，Singleton
3. **`IDbProvider` 实现** → Scrutor 扫描，`AsImplementedInterfaces`，Singleton
4. **特殊窗口**（如 LoginWindow）→ 手动 `AddTransient`
5. **配置选项** → `services.Configure<T>(configuration.GetSection(...))`
6. **UI 框架基础设施** → 手动注册 NavigationService, ContentDialogService 等
7. **不遵循命名规范的服务** → 手动注册

构造函数注入新依赖时，确保对应的服务/ViewModel/Page 已存在于上述自动扫描范围内，即可自动注入。

---

## 2. 数据库规范

### 2.1 命名规范（强制）

> **新表必须遵守。旧表逐步迁移。**

| 对象 | 规范 | 示例 |
|------|------|------|
| 表名 | 小写 + 下划线分隔，单数/复数统一 | `cost_process`, `bom_item` |
| 主键 | 统一 `id` | `id` |
| 外键 | 关联表名 + `_id` | `scheme_id`, `product_code` |
| 普通字段 | 小写 + 下划线，禁止关键字 | `material_code`, `process_type`, `sort_order` |
| 时间字段 | `created_at`, `updated_at` | — |
| 布尔字段 | `is_` / `has_` 前缀 | `is_active`, `is_deleted`, `has_approved` |
| C# 实体 | PascalCase，与表列名一致 | `CreatedAt`, `IsActive` |
| Dapper 映射 | `DefaultTypeMap.MatchNamesWithUnderscores = true` | 自动转换 `created_at` ↔ `CreatedAt` |
| `[Table]` 特性 | `using System.ComponentModel.DataAnnotations.Schema;` | 显式声明实体对应的数据库表名 |

### 2.2 实体映射规则（强制）

- **每个数据库表必须有对应的实体类，实体类必须标注 `[Table("表名")]` 特性**
- **所有实体类和字段必须有 `<summary>` 说明**
- **所有 `public` 类、接口、方法必须有 `<summary>` 说明**，私有方法酌情添加
- 查询使用 `QueryAsync<Entity>()` 映射实体，禁止用元组/匿名类型接收结果
- **插入用 `entity.ToInsertDict()` 生成字典**（EntityHelper 根据 `[Column]` 特性映射列名，跳过 Id），禁止手写列名字符串

### 2.3 性能规则（强制）

- **禁止 N+1 查询**：循环内禁止逐条 SQL 查询，必须使用 `WHERE ... IN (...)` 批量查询，一次取回所有数据后再内存匹配
- **递归内禁止 IO**：递归方法内禁止数据库查询或 HTTP 调用，必须在递归前批量取数据，通过参数传入字典/集合
- **批量写入用事务**：多条 INSERT/UPDATE 必须包裹在 `BeginTransaction`/`Commit` 内，减少磁盘 flush 次数
- **SQL 查询只取需要的列**：禁止 `SELECT *` 取全部列，除非确实需要所有字段
- **大数据分页**：列表查询必须分页，禁止一次加载全部数据到内存
- **合理使用索引列过滤**：WHERE 条件必须优先命中索引列

### 2.4 跨数据库兼容（SqlKata 查询构建）

- **DML 统一使用 SqlKata Query 对象**：所有 CRUD SQL 必须通过 `new Query("表名").Where(...).OrderBy(...)` 链式构建，禁止手写 SQL 字符串
  - SqlKata 自动根据 `DatabaseProviderType` 选择正确的 Compiler（SqliteCompiler / SqlServerCompiler / MySqlCompiler）
  - 分页：用 `.Limit(n).Offset(m)` SqlKata 自动转为对应方言
  - 自增ID：插入后用 `SqlDialect.LastInsertIdSql` 获取
  - 批量 IN 查询：用 `.WhereIn("col", list)` SqlKata 自动展开参数
- **DDL 按数据库分实现**：`CREATE TABLE` 脚本通过 `ISchemaProvider` 接口按 `DatabaseProviderType` 分别提供，DDL 保留手写 SQL（SqlKata 不支持 DDL）
- **日期时间**：代码统一用 `DateTime.Now` 赋值给参数，不依赖数据库默认值

---

## 3. 日志规范 (Serilog)

### 3.1 三级日志体系

| 日志 | 文件 | 级别 | 用途 |
|------|------|------|------|
| 业务日志 | `logs/app-.log` | Information+ | 正常业务操作、SQL 查询、应用启停 |
| 错误日志 | `logs/errors-.log` | Warning+ | 异常和警告 |
| 调试日志 | `logs/debug-.log` | Verbose（独立管道） | 临时埋点，问题解决后清理 |

### 3.2 日志规则（强制）

- **业务操作日志**：用 `Log.Information` / `Log.Warning` / `Log.Error`，记录用户可见的操作结果、异常
- **SQL 日志**：由 `SqlLogger` 自动记录（Information 级别），无需手动调用
- **调试埋点**：用 `DebugLogger.Trace("模块", "消息", 参数...)`，仅 Debug 编译时启用。排查问题时临时添加，问题解决后**必须清理**
  - **三次原则（强制）**：同一问题如果**三次尝试**仍未解决，必须写调试日志排查，禁止继续盲改。占位符用 `{0}` `{1}` 序号格式（Serilog `params object[]`）
- **错误必须记录日志**：所有 `catch (Exception ex)` 必须调用 `Log.Error(ex, "描述")` 写入日志，禁止静默吞异常或仅弹窗不记日志
- **禁止**：用 `Log.Information` 记录循环内逐条明细、中间计算步骤等调试型日志

---

## 4. 第三方库选择原则（强制）

- **优先用库，避免自研**：实现功能时优先使用社区成熟、安全可靠、开源免费的第三方库，避免重复造轮子
- **选型标准**：
  - 开源免费（Apache/MIT/BSD 协议）
  - GitHub stars > 1k，持续维护（最近 6 个月有更新）
  - 无已知安全漏洞（NuGet 无关键 vulnerability 警告）
  - 在本项目适用的场景有充分文档和社区支持
- **推荐库**（按场景）：

| 场景 | 推荐库 |
|------|--------|
| 跨数据库 SQL 构建 | SqlKata |
| Excel 读写 | MiniExcel |
| MVVM 工具 | CommunityToolkit.Mvvm |
| ORM / 数据访问 | Dapper |
| 日志 | Serilog |
| 对象映射 / DTO 克隆 | Mapster（禁止手写逐属性复制） |
| 输入校验 | FluentValidation（禁止散落 if/return 校验） |
| 设备指纹 / 硬件ID | DeviceId |

---

## 5. WPF/MVVM 页面布局规范

### 5.1 核心原则

1. **禁止 TabControl 嵌套** — 页面最多使用 1 层 TabControl，如需分类切换，使用"分类栏 + ContentControl"模式
2. **引导式布局** — 用户进入页面后应一眼看懂操作流程，每个区域有标题和功能描述
3. **扁平化层级** — 用侧边栏/分类栏替代纵向 Tab 标签，减少视觉层级深度
4. **即时反馈** — 列表为空时显示操作引导，异步操作时显示 ProgressRing（仅操作中可见）

### 5.2 主页面模式：侧边栏 + 内容区

用于包含多个子模块的顶层页面：

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />  <!-- 顶部标题栏 -->
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <!-- 标题栏 -->
    <Border Grid.Row="0" Padding="12,10" Background="#F5F5F5">
        <StackPanel Orientation="Horizontal">
            <ui:SymbolIcon FontSize="22" Symbol="..." />
            <StackPanel>
                <TextBlock FontSize="15" FontWeight="SemiBold" Text="模块名称" />
                <TextBlock Foreground="Gray" FontSize="12" Text="简要流程说明" />
            </StackPanel>
        </StackPanel>
    </Border>

    <Grid Grid.Row="1">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="220" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <!-- 左侧步骤导航 -->
        <Border Grid.Column="0" Background="#F0F0F0">
            <StackPanel>
                <TextBlock Margin="12,12,12,4" FontSize="11" Foreground="Gray" Text="操作流程" />
                <ListBox ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                         SelectedIndex="{Binding ViewModel.SelectedTabIndex}">
                    <!-- ItemTemplate: 序号 + 标题 + 描述 -->
                </ListBox>
            </StackPanel>
        </Border>

        <!-- 右侧内容区：ContentControl + DataTrigger -->
        <Border Grid.Column="1" Background="Transparent">
            <ContentControl>
                <ContentControl.Style>
                    <Style TargetType="ContentControl">
                        <Style.Triggers>
                            <DataTrigger Binding="..." Value="0">
                                <Setter Property="Content">
                                    <Setter.Value><views:SubView1 /></Setter.Value>
                                </Setter>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </ContentControl.Style>
            </ContentControl>
        </Border>
    </Grid>
</Grid>
```

**ListBoxItem 模板规范** — 选中态 `#0078D4`，悬停态 `#E0E0E0`，圆角 6px，禁用水平滚动条。

### 5.3 子页面模式：顶部分类栏 + 内容区

用于有多个分类的子页面：

```xml
<Grid Margin="8">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />  <!-- 标题栏 -->
        <RowDefinition Height="Auto" />  <!-- 分类导航栏 -->
        <RowDefinition Height="*" />     <!-- 内容区 -->
        <RowDefinition Height="Auto" />  <!-- InfoBar -->
    </Grid.RowDefinitions>

    <!-- 标题栏 -->
    <StackPanel Grid.Row="0" Margin="0,4,0,8" Orientation="Horizontal">
        <TextBlock FontSize="14" FontWeight="SemiBold" Text="模块名" />
        <TextBlock Margin="8,0,0,0" Foreground="Gray" FontSize="12" Text="— 功能描述" />
    </StackPanel>

    <!-- 分类导航栏：ListBox pill 按钮组 -->
    <Border Grid.Row="1" Padding="4" Background="#F0F0F0" CornerRadius="6">
        <ListBox BorderThickness="0" Background="Transparent"
                 SelectedIndex="{Binding ViewModel.SelectedSectionIndex}">
            <!-- 每项带图标 + 文字 -->
        </ListBox>
    </Border>

    <!-- 内容区 -->
    <ContentControl Grid.Row="2">
        <!-- DataTrigger 切换各分类内容 -->
    </ContentControl>
</Grid>
```

### 5.4 嵌套视图：必须用 UserControl 而非 Page

WPF `Page` 只能作为 `Window`/`Frame` 的直接子级。嵌入 TabControl、ContentControl 或任何容器中的子视图**必须使用 `UserControl`**。

```csharp
public partial class SomeSubView : UserControl
{
    public SomeViewModel ViewModel { get; }

    // 无参构造函数：从 DI 解析 ViewModel（为了 XAML 可直接嵌入）
    public SomeSubView() : this(App.Services.GetRequiredService<SomeViewModel>()) { }

    // 带参构造函数：DI 和代码创建使用
    public SomeSubView(SomeViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
```

### 5.5 控件规范

#### ComboBox：中文显示 + 英文键值

```xml
<ComboBox SelectedValuePath="Tag" SelectedValue="{Binding ViewModel.SomeProperty}">
    <ComboBoxItem Content="中文显示" Tag="EnglishKey" />
    <ComboBoxItem Content="全部"     Tag="" />  <!-- 空串表示不过滤 -->
</ComboBox>
```

DataGrid 中展示英文键值的列，通过 `IValueConverter` 转为中文显示。

#### ProgressRing

必须同时绑定 `Visibility`，仅在操作进行中显示：

```xml
<ui:ProgressRing IsIndeterminate="{Binding ViewModel.IsWorking}"
                 Visibility="{Binding ViewModel.IsWorking, Converter={StaticResource BoolToVis}}" />
```

#### 集合属性：必须用 ObservableCollection

```csharp
// ✅ 正确
public ObservableCollection<Item> Details { get; set; } = new();

// ❌ 错误：添加删除不会更新 UI
public List<Item> Details { get; set; } = new();
```

### 5.6 颜色规范

禁止使用 `{DynamicResource WpfUiThemeKey}`，统一使用固定色值：

| 用途 | 色值 |
|------|------|
| 侧边栏/分类栏背景 | `#F0F0F0` |
| 标题栏/卡片背景 | `#F5F5F5` |
| 选中态高亮 | `#0078D4` |
| 悬停态 | `#E0E0E0` |
| 内容区背景 | `Transparent` |
| 弹窗遮罩 | `#80000000` |

### 5.7 ViewModel 模式

```csharp
public partial class SomeViewModel : ObservableObject
{
    // 分类切换
    [ObservableProperty] private int _selectedSectionIndex;

    // 列表数据用 ObservableCollection
    [ObservableProperty] private ObservableCollection<Item> _items = new();

    // 弹窗控制
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private Item? _editingItem;

    // 异步状态
    [ObservableProperty] private bool _isWorking;

    // InfoBar
    [ObservableProperty] private bool _showInfoBar;
    [ObservableProperty] private string _infoBarTitle = "";
    [ObservableProperty] private string _infoBarMessage = "";
    [ObservableProperty] private InfoBarSeverity _infoBarSeverity;
}
```

---

## 6. 命名约定

- 接口: `I{ServiceName}` → 实现: `{ServiceName}Service`
- ViewModel: `{PageName}ViewModel`
- Page: `{PageName}Page`
- 子视图: `{Parent}{Section}View` (UserControl)

---

## 7. 异常处理规范（强制）

### 7.1 核心原则

| 规则 | 说明 |
|------|------|
| **禁止静默吞异常** | 所有 `catch` 必须记录日志或重新抛出，空的 `catch {}` 是 bug |
| **异常信息必含上下文** | 日志必须包含操作名称和关键参数值，不能只记录 `ex.Message` |
| **UI 层统一处理** | 未预料的异常在 UI 层有全局兜底（`DispatcherUnhandledException` / `UnobservedTaskException`），避免进程崩溃 |
| **业务异常 vs 系统异常** | 预期内的业务校验失败用返回值/Result 模式；预期外的系统异常才 throw |

### 7.2 分层处理策略

```
View 层     → try-catch，弹窗/InfoBar 提示用户，记 Error 日志
ViewModel  → try-catch 异步命令，设置 IsError 状态，不直接弹窗
Service    → 捕获外部调用异常，包装为业务异常或 ServiceResult.Error 返回
Data 层    → 不 catch，让异常向上传播到 Service 层统一处理
```

### 7.3 async void 禁令

```csharp
// ❌ 禁止：async void 异常无法被捕获，会直接崩溃
public async void LoadData() { ... }

// ✅ 正确：用 RelayCommand 的 async 委托（CommunityToolkit 自动处理）
[RelayCommand]
private async Task LoadDataAsync() { ... }
```

---

## 8. 配置文件管理

### 8.1 分层配置

```
appsettings.json              # 内置默认配置（入库，随版本发布）
└── user-settings.json        # 用户个人配置（%LocalAppData%，不入库，优先级更高）
```

- **内置配置**：定义程序运行所必需的最低配置和出厂默认值
- **用户配置**：用户在设置页面的修改写入此文件，覆盖内置值
- **加密存储**：敏感数据（授权、密钥）用 DPAPI/平台加密 API 单独存储，不混入配置文件

### 8.2 配置注入

```csharp
// appsettings.json 中的节直接绑定到强类型配置对象
services.Configure<AppConfiguration>(configuration.GetSection("AppConfiguration"));
// 用户配置通过 UserSettingsService 在运行时加载并覆盖
```

---

## 9. Commit 规范

### 9.1 消息格式

```
<type>: <简短描述>

<详细说明（可选）>

Co-Authored-By: Claude Code <noreply@anthropic.com>
```

| Type | 用途 |
|------|------|
| `feat` | 新功能 |
| `fix` | Bug 修复 |
| `refactor` | 重构（无功能变化） |
| `perf` | 性能优化 |
| `style` | 格式/代码风格 |
| `docs` | 文档/注释 |
| `test` | 测试用例 |
| `chore` | 构建/工具/依赖 |

### 9.2 规则

- 一行标题不超过 **70 个字符**，中文优先
- 描述"为什么"改，而非"改了什么"（diff 本身已经展示了改动内容）
- **禁止空 commit**：没有实质性变更不创建 commit
- Commit 和 PR 不应包含 .env、密钥、二进制文件

---

## 附录：应用启动流程规范

> **启动规则（强制）**：`StartAsync` 中只能放必须的初始化逻辑（数据库建表、授权验证、主窗口启动）。禁止在启动流程中添加数据清理、缓存预热、一次性修复脚本、统计汇总等非必要动作。一次性数据修复应通过 SQL 脚本手动执行。
