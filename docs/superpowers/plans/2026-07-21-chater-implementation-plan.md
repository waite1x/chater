# Chater 正式实现计划

> 关联设计：[Chater 设计文档](../specs/2026-07-21-chater-design.md)  
> 当前基线：Avalonia 模板；尚无业务代码与测试

## 执行原则

- 每个工作包完成后必须保持 `dotnet build` 和相应测试通过；禁止将未编译的半成品带入下一包。
- 数据库 schema 只能通过新增迁移演进；不能修改已发布迁移。
- 任意聊天会话、provider 配置、技能快照和 MAF session 的持久化写入必须可取消、可诊断且不泄漏 API Key。
- UI 依赖服务，服务依赖数据/平台/provider 适配器；严禁反向引用。
- 每增加一个 NuGet 依赖，都要在四个 Release Native AOT RID 上验证并锁定版本。

## 依赖顺序

```
P0 工程基线
 ├─ P1 领域模型、SQLite、迁移、日志
 ├─ P2 应用组合根与设置基础
 │   └─ P3 Provider 管理与连接测试
 │       └─ P4 MAF Session、会话与消息服务
 │           └─ P5 ChatWindow 与完整聊天链路
 ├─ P6 技能与历史会话管理 ────────────────┘
 ├─ P7 Windows/macOS 平台能力 ────────────┘
 └─ P8 Native AOT、安装与发布质量（贯穿，最终收口）
```

## P0：工程基线与构建矩阵

目标：将模板改造为可持续交付的正式应用骨架。

### 修改与新增

- 更新 `Chater/Chater.csproj`：锁定所有包版本，加入 DI、日志、SQLite、MAF 与 provider 所需依赖；配置 `Nullable`、分析器、Release AOT 属性。
- 新增 `Directory.Build.props`：统一警告策略、语言版本、确定性构建、AOT/trim 分析开关。
- 新增 `Chater.Tests/Chater.Tests.csproj` 与 `Chater.Tests/`；将其加入 `Chater.sln`。
- 新增 `.github/workflows/build.yml`（或现有 CI 目录）：Windows/macOS 测试和 `win-x64`、`win-arm64`、`osx-x64`、`osx-arm64` 发布矩阵。
- 新增 `docs/release/`：RID、签名、符号文件和发布产物规范。

### 完成标准

- Debug build/test 与四个 RID 的 Release publish 可运行；
- 项目没有浮动 NuGet 版本或未处理的 AOT/trim 警告；
- CI 保存发布工件、日志和测试结果。

## P1：领域模型、SQLite、迁移与日志

目标：实现一切业务功能共用的持久化与诊断基础。

### 文件落点

```
Chater/
├── Models/
│   ├── ApiProvider.cs
│   ├── Skill.cs
│   ├── Conversation.cs
│   ├── Message.cs
│   ├── AppSetting.cs
│   └── Enums/*.cs
├── Data/
│   ├── SqliteDatabase.cs
│   ├── DatabaseMigrator.cs
│   ├── Migrations/0001_InitialSchema.sql
│   ├── ApiProviderRepository.cs
│   ├── SkillRepository.cs
│   ├── ConversationRepository.cs
│   ├── MessageRepository.cs
│   └── AppSettingRepository.cs
├── Services/
│   ├── AppPaths.cs
│   ├── DatabaseService.cs
│   └── StartupRecoveryService.cs
└── Logging/LogRedaction.cs
```

### 实施任务

1. `AppPaths` 解析 Windows/macOS 的应用数据、日志、数据库和导出目录，并创建当前用户专属目录。
2. `SqliteDatabase` 提供连接创建、`foreign_keys=ON`、事务执行和统一参数绑定。
3. `DatabaseMigrator` 使用 schema version 表逐项执行嵌入式 SQL 迁移；失败不更新版本号。
4. 按设计文档创建五张业务表、索引和内置技能种子数据。
5. 实现具体仓储：provider/skill CRUD、conversation 检索归档、message 顺序追加与状态更新、settings 读写。
6. 实现启动恢复：将遗留 `Pending`/`Streaming` 消息置为 `Cancelled`。
7. 记录结构化日志，应用 `LogRedaction` 屏蔽 API Key、Authorization 和数据库中的敏感字段。

### 测试

- 在临时 SQLite 文件上验证首次迁移、重复启动、迁移失败回滚与外键约束；
- 验证 message 序列号并发写入、会话删除级联、provider/skill 有引用时仅能禁用；
- 验证日志不会输出 API Key 或 Authorization 值。

### 完成标准

数据库可从空目录初始化并重复打开；所有数据访问为参数化 SQL；测试覆盖正常、异常和事务回滚路径。

## P2：组合根、应用生命周期与设置外壳

目标：用 DI 替换模板中的 `new MainWindowViewModel()`，建立服务生命周期和设置导航。

### 修改与新增

- 修改 `Program.cs`：只保留 Avalonia 引导，不能在 UI 初始化前创建依赖服务。
- 修改 `App.axaml.cs`：构建 `ServiceProvider`，执行数据库迁移与启动恢复，注册生命周期回调；关闭时按顺序停止聊天、保存 session、关闭数据库和日志。
- 新增 `Composition/ServiceCollectionExtensions.cs`：集中注册数据、服务、provider 和平台适配器。
- 重写 `Views/MainWindow.axaml`、`ViewModels/MainWindowViewModel.cs`，并新增 `SettingsNavigationViewModel`、`SettingsPage` 枚举及空页面骨架。
- 删除模板 `Greeting` 绑定与模板图标引用，替换为产品资源。

### 测试

- 测试服务注册能创建全部 singleton/scoped 服务；
- 测试应用关闭顺序：停止活动请求 → 持久化 session → 释放平台资源 → 关闭数据库；
- Avalonia headless 测试设置导航与空状态。

### 完成标准

应用启动后展示设置窗口；初始化/迁移失败有用户可读提示和安全日志；应用退出不丢失已完成数据。

## P3：Provider 管理与连接测试

目标：让用户配置全部目标 provider，并安全地诊断连接。

### 文件落点

```
Chater/
├── Providers/
│   ├── IChatGateway.cs
│   ├── AgentFactory.cs
│   ├── OpenAiGateway.cs
│   ├── AnthropicGateway.cs
│   ├── OllamaGateway.cs
│   ├── OpenAiCompatibleGateway.cs
│   └── ProviderErrorMapper.cs
├── Services/ProviderService.cs
├── ViewModels/Providers/
│   ├── ProviderListViewModel.cs
│   └── ProviderEditorViewModel.cs
└── Views/Settings/ProvidersView.axaml
```

### 实施任务

1. 完成 provider 列表、编辑表单、默认 provider、启用/禁用和删除确认交互。
2. API Key 直接写入 `ApiProviders.ApiKey`；展示时始终掩码，编辑时只有用户主动输入才替换原值。
3. 各 gateway 将 provider 配置转换为对应 SDK client，并提供统一的 `TestConnectionAsync`。
4. 连接测试采用短请求和超时；将认证、配置、DNS、网络、限流、服务端失败转换成稳定错误码。
5. 一条事务内维护唯一默认 provider；禁用/删除默认 provider 时要求用户选择替代项或允许空默认项。

### 测试

- 仓储测试默认项唯一性、掩码模型和删除规则；
- 使用 HTTP mock 覆盖每个 gateway 的成功、401/403、429、5xx、超时和无效响应；
- UI 测试表单验证与连接测试状态。

### 完成标准

用户可管理四类 provider 并获得准确可操作的连接结果；任何日志或 UI 诊断均不泄漏 API Key。

## P4：MAF Session、会话与消息服务

目标：形成持久化多轮会话的后端核心，尚不依赖完整聊天 UI。

### 文件落点

```
Chater/
├── Services/
│   ├── ConversationService.cs
│   ├── MessageService.cs
│   ├── ChatService.cs
│   ├── SessionCache.cs
│   └── SessionStateValidator.cs
└── Models/
    ├── SessionSnapshot.cs
    ├── ChatRunResult.cs
    └── ErrorCode.cs
```

### 实施任务

1. `ConversationService.CreateAsync` 冻结 provider/model/endpoint 和 skill 版本快照，创建 `AIAgent` 与 `AgentSession`，并写入配置指纹、MAF 版本、序列化 session。
2. `SessionCache` 以 conversation ID 维护内存 session，使用每会话异步锁保证同一会话只运行一个请求。
3. `SessionStateValidator` 在恢复前校验 agent 类型、provider、模型、技能版本、MAF 版本和 hash；不匹配时标记 `Invalid`。
4. `ChatService.SendStreamingAsync` 保存用户消息/占位助手消息，枚举 `RunStreamingAsync`，输出 UI 增量并批量写库。
5. 生成结束、取消、失败时，原子更新消息终态、`UpdatedAt` 和 `SessionState`；对临时 5xx/429 实施有限重试。
6. `ConversationService.RestoreAsync` 通过相同 agent 配置调用反序列化；失败后保留可见历史并提供创建新会话入口。

### 测试

- Fake `IChatGateway` 和 fake `AIAgent` 测试两轮上下文、序列化/恢复、配置 hash 失配、并发发送拒绝；
- 测试首 token、连续增量、取消、超时、429 重试、非重试错误和进程中断恢复；
- 断言每一终态下 messages 与 session snapshot 的事务一致性。

### 完成标准

服务层不依赖窗口即可完成“创建会话 → 两轮流式对话 → 退出 → 恢复 → 继续”的集成测试。

## P5：ChatWindow 与完整聊天体验

目标：把 P4 服务变为用户可用的流式聊天窗口。

### 实施任务

1. 新建 `Views/ChatWindow.axaml`、`ViewModels/ChatWindowViewModel.cs`、`MessageItemViewModel.cs` 与 Markdown 渲染控件。
2. 绑定 provider、模型、skill 下拉选择；修改选择项时新建会话，不复用原 session。
3. 绑定消息列表、输入框、Enter/Shift+Enter、发送、停止、重试、复制最后回复和代码块复制命令。
4. 为流式增量引入 UI 更新节流和末尾自动滚动；用户主动向上滚动时暂停自动滚动并提供“回到底部”按钮。
5. 实现无 provider、空输入、加载、认证失败、网络错误、取消和已归档会话的完整状态。
6. 添加会话历史抽屉：搜索、打开、重命名、归档、删除和继续最近会话。

### 测试

- Avalonia headless 测试命令启用状态、输入快捷键、流式气泡、停止/重试和错误提示；
- 端到端测试一条真实 mock 流：配置 provider → 创建会话 → 发送 → 停止 → 重试 → 重启 → 继续；
- 人工审查 Windows/macOS 的键盘导航、缩放、高 DPI 和暗/亮主题。

### 完成标准

所有聊天与历史功能在两个平台上可完整操作，且服务层异常不会使窗口失去可用性。

## P6：技能体系

目标：实现内置技能和自定义技能的完整管理及会话快照规则。

### 实施任务

1. 初始化五个内置技能，禁止删除并允许启用/禁用和排序。
2. 实现自定义技能编辑器，验证名称唯一性、system prompt 非空和长度限制。
3. 会话创建时写入 skill ID、版本和完整指令快照；编辑 skill 后增加 `Version`，不改变历史会话使用的 prompt。
4. 在聊天窗口中切换技能时创建新会话；在历史会话中展示原技能快照。

### 测试与完成标准

- 验证内置技能保护、排序、禁用、版本递增和历史会话不可变；
- 验证更新技能不会改变既有 MAF Session 的配置指纹或上下文。

## P7：Windows/macOS 平台能力

目标：完成正式桌面应用的呼出、托盘、权限与单实例体验。

### 文件落点

```
Chater/Platform/
├── IHotkeyService.cs
├── ITrayService.cs
├── ISingleInstanceService.cs
├── Windows/
└── macOS/
```

### 实施任务

1. 实现并注册 Windows `RegisterHotKey`、macOS 系统事件监控的快捷键服务。
2. 提供快捷键格式化、修改、冲突检测、注册失败恢复和 macOS 辅助功能权限引导。
3. 实现托盘显示设置、显示聊天、新建会话、退出；将生命周期命令连接到 `AppLifecycleService`。
4. 实现单实例 IPC：第二个实例向第一个实例发送激活聊天窗口命令后退出。
5. 将平台 API 完全隔离，避免非目标平台加载平台原生库。

### 测试与完成标准

- 单元测试平台无关的快捷键解析与状态机；
- 每个目标平台人工验证注册、冲突、权限、托盘、二次启动、Esc 隐藏和退出；
- 退出后没有残留进程、快捷键注册或数据库锁。

## P8：Native AOT、安装与发布质量

目标：收口为可正式分发、可升级、可诊断的产品。

### 实施任务

1. 在每次 CI publish 处理 AOT/trimming 警告；以 Release 原生工件执行测试和启动冒烟，而非只测试 IL build。
2. 配置 Windows 签名和 macOS 签名/notarization，保留 `.pdb`、`.dSYM`、SBOM、校验和和版本清单。
3. 建立安装、升级、卸载与数据保留策略；卸载程序必须提供保留/删除本地数据选项。
4. 加入崩溃日志、诊断包脱敏、性能基准和数据库备份/恢复验证。
5. 完成隐私说明、第三方许可证、更新策略和发布清单。

### 发布门槛

- 四个 RID 的 Native AOT 构建、安装、启动和核心聊天冒烟均成功；
- P1–P7 自动化与人工测试全部通过，无阻断级缺陷；
- 无 API Key 泄漏路径，迁移和强制退出恢复测试通过；
- 签名、notarization、SBOM、符号文件和校验和已随发布工件产出。

## 首个实施任务

从 P0 开始，提交边界为“工程基线与测试项目”：先引入锁定版本的基础依赖、建立 `Chater.Tests`、启用分析器和 Release AOT 配置，再配置四 RID CI publish。该提交不实现业务 UI 或数据库，完成后再进入 P1。
