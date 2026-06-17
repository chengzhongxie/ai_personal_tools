# Claude Conventions Library

可复用的 AI 编码助手项目规范模板，适用于 **.NET + WPF + MVVM** 技术栈的桌面应用。

## 使用方式（Git Submodule）

```bash
# 初始化
git submodule add https://github.com/<your-username>/claude-conventions-library.git docs/template
```

```markdown
<!-- 项目 CLAUDE.md 中引用 -->
通用规范参见 [docs/template/CLAUDE_TEMPLATE.md](docs/template/CLAUDE_TEMPLATE.md)
```

```bash
# 拉取模板更新
git submodule update --remote docs/template
```

## 模板内容

| 章节 | 内容 |
|------|------|
| 0. 安全护栏 | 数据库/ Git /代码边界/安全编码/测试/行为准则 |
| 1. 架构设计 | DDD 目录结构、单一职责、DI 注册模式 |
| 2. 数据库规范 | 命名、实体映射、性能规则、跨库兼容 |
| 3. 日志规范 | Serilog 三级日志体系、调试策略 |
| 4. 库选择原则 | 选型标准 + 推荐库对照表 |
| 5. WPF/MVVM 布局 | 侧边栏/分类栏/UserControl/ViewModel/颜色规范 |
| 6. 命名约定 | 接口/服务/ViewModel/Page 命名模式 |
| 7. 异常处理 | 分层处理策略、async void 禁令 |
| 8. 配置文件管理 | 双层配置覆盖、敏感数据隔离 |
| 9. Commit 规范 | 消息格式、type 分类、规则约束 |

## 维护

本仓库是模板的**唯一源**。各项目通过 submodule 引用，更新后项目端执行 `git submodule update --remote` 拉取最新版本。
