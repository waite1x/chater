# Chater — AI 桌面聊天应用设计文档

> 日期：2026-07-21｜状态：正式实现方案｜版本：1.0

## 1. 产品目标与范围

Chater 是面向个人用户的原生桌面 AI 聊天应用。用户通过全局快捷键或系统托盘快速呼出聊天窗口，选择模型与技能，进行可持久化的流式多轮对话。设置窗口提供 API 提供商、技能、快捷键与应用信息管理。

首发平台为 Windows 和 macOS，均以 Native AOT 自包含应用交付。首发版本完整交付以下功能：

- OpenAI、Anthropic、Ollama 和 OpenAI 兼容 API 提供商的配置、连接测试、模型选择和默认项设置；
- 通用对话、翻译、总结、代码解释、润色等内置技能，以及自定义技能的新增、编辑、排序、禁用和删除；
- 新建、继续、搜索、切换、重命名、归档、删除会话，以及完整消息历史；
- 基于 Microsoft Agent Framework（MAF）`AgentSession` 的多轮上下文与重启恢复；
- 流式输出、Markdown 渲染、复制最后回复、取消生成、自动滚动和错误重试；
- Windows/macOS 全局快捷键、托盘菜单、Esc 隐藏、单实例和启动行为；
- SQLite 本地数据存储、数据库迁移、日志、崩溃诊断、自动更新预留和正式发布流水线。

云同步、账号体系、团队协作、文件/图片/音频输入、工具调用和 Agent Workflow 不属于当前产品需求；新增这些能力时必须另行设计其权限、数据同步和兼容性模型。

## 2. 已确认技术决策

| 领域 | 决策 |
|---|---|
| UI | Avalonia 12 + Fluent Theme + CommunityToolkit.Mvvm |
| 运行时 | .NET 10，Native AOT，自包含发布 |
| 首发平台 | Windows x64 / Arm64；macOS x64 / Arm64 |
| AI 编排 | Microsoft Agent Framework（MAF），每个会话对应一个 `AgentSession` |
| 流式调用 | `AIAgent.RunStreamingAsync`，将 `AgentResponseUpdate` 增量写入 UI 和数据库 |
| 数据库 | `Microsoft.Data.Sqlite`，手写参数化 SQL 与版本化迁移 |
| 密钥存储 | API Key 直接保存在 SQLite `ApiProviders.ApiKey`；不使用操作系统密钥库 |
| 依赖管理 | 所有 NuGet 包版本必须锁定；禁止使用 `latest` |
| 依赖注入 | `Microsoft.Extensions.DependencyInjection`，在应用启动时完成组合根注册 |

### 数据安全边界

按产品要求，API Key 和聊天内容直接保存在 SQLite。数据库必须置于当前用户的应用数据目录，创建时限制为当前用户可读写；API Key 不得出现在日志、异常弹窗、诊断包、导出文件或剪贴板。用户删除 provider 或执行“清除本地数据”时，必须连同对应 API Key 一并删除。该策略不提供操作系统级凭据隔离，产品设置页应明确告知用户“密钥保存在本地数据库”。

## 3. 架构

```
┌──────────────────────────────────────────────────────────────┐
│                         Avalonia UI                            │
│  MainWindow（设置）    ChatWindow（聊天）    Tray / Dialogs     │
├──────────────────────────────────────────────────────────────┤
│ ViewModels：Settings / Provider / Skill / Conversation / Chat │
├──────────────────────────────────────────────────────────────┤
│ Application Services                                           │
│ ChatService  ConversationService  ProviderService  SkillService│
│ MessageService  DatabaseService  AppLifecycleService           │
├─────────────────────────────┬────────────────────────────────┤
│ MAF Provider Adapter         │ Platform Adapter               │
│ AgentFactory / ChatGateway   │ HotkeyService / TrayService    │
│ AIAgent / AgentSession       │ Windows / macOS implementations│
├─────────────────────────────┴────────────────────────────────┤
│ SQLite：providers / skills / conversations / messages / settings│
└──────────────────────────────────────────────────────────────┘
```

UI 层不得直接访问 SQLite、MAF 或平台 API。ViewModel 仅协调命令和可观察状态；所有 I/O、会话状态变更和事务在服务层完成。

对外部变化点保留最小接口：`IChatGateway`、`IHotkeyService`、`ITrayService`、`IPlatformPaths`。业务服务与 SQLite 仓储保持具体类，避免泛型仓储掩盖事务、排序和查询语义。

## 4. MAF Session 与会话生命周期

`Conversation` 是 Chater 的持久化会话实体，`AgentSession` 是当前 agent 的运行时上下文。一个 conversation 固定绑定一个 provider、模型、技能版本与 agent 配置指纹；它们发生变化时必须创建新会话，而不是把旧 Session 交给新 agent。

```
创建会话
  → AgentFactory 创建 AIAgent
  → agent.CreateSessionAsync(conversationId)
  → 保存 Conversation 与 Session 元数据

发送消息
  → 保存 user Message（Pending）
  → agent.RunStreamingAsync(message, session, cancellationToken)
  → 聚合 AgentResponseUpdate，增量更新 assistant Message（Streaming）
  → 完成后将消息设为 Completed，agent.SerializeSession(session) 写入 SQLite

应用重启 / 打开历史会话
  → 根据 Conversation 重新创建相同配置的 AIAgent
  → agent.DeserializeSessionAsync(serializedSession)
  → 成功：恢复 AgentSession；失败：会话标记为需要恢复，提示用户重试或从历史重新开始
```

每个打开的会话在内存中保留一个 `AgentSession` 缓存；切换或关闭窗口不销毁它，应用退出前统一序列化。发送、取消、结束和退出均以数据库事务更新消息状态与 `Conversations.UpdatedAt`。同一会话只允许一个活动运行；再次发送必须先取消或等待当前运行完成。

`SessionState` 必须包含 MAF 序列化内容、MAF 包版本、agent 类型、provider 类型、模型、技能版本和配置指纹。反序列化前逐项校验；不匹配时不尝试恢复，避免将 session 用于错误的模型或 provider。消息表仍保存全部可见历史，作为 UI 展示、审计和用户导出的来源。

## 5. 数据模型

```sql
PRAGMA foreign_keys = ON;

CREATE TABLE ApiProviders (
    Id            TEXT PRIMARY KEY,
    Name          TEXT NOT NULL,
    ProviderType  INTEGER NOT NULL, -- OpenAI=0, Anthropic=1, Ollama=2, OpenAICompatible=3
    ApiKey        TEXT NOT NULL,
    Endpoint      TEXT,
    ModelId       TEXT NOT NULL,
    IsDefault     INTEGER NOT NULL DEFAULT 0,
    IsEnabled     INTEGER NOT NULL DEFAULT 1,
    CreatedAt     TEXT NOT NULL,
    UpdatedAt     TEXT NOT NULL
);

CREATE TABLE Skills (
    Id            TEXT PRIMARY KEY,
    Name          TEXT NOT NULL,
    Description   TEXT,
    SystemPrompt  TEXT NOT NULL,
    Icon          TEXT,
    IsBuiltIn     INTEGER NOT NULL DEFAULT 0,
    IsEnabled     INTEGER NOT NULL DEFAULT 1,
    SortOrder     INTEGER NOT NULL DEFAULT 0,
    Version       INTEGER NOT NULL DEFAULT 1,
    CreatedAt     TEXT NOT NULL,
    UpdatedAt     TEXT NOT NULL
);

CREATE TABLE Conversations (
    Id                    TEXT PRIMARY KEY,
    Title                 TEXT NOT NULL,
    ProviderId            TEXT NOT NULL REFERENCES ApiProviders(Id),
    SkillId               TEXT REFERENCES Skills(Id),
    ProviderConfiguration TEXT NOT NULL, -- 创建时快照，含 provider/model/endpoint，不含 ApiKey
    SkillVersion          INTEGER,
    AgentType             TEXT NOT NULL,
    AgentConfigurationHash TEXT NOT NULL,
    MafVersion            TEXT NOT NULL,
    SessionState          TEXT NOT NULL,
    SessionStatus         TEXT NOT NULL, -- Active/Restorable/Invalid/Failed
    IsArchived            INTEGER NOT NULL DEFAULT 0,
    CreatedAt             TEXT NOT NULL,
    UpdatedAt             TEXT NOT NULL
);

CREATE TABLE Messages (
    Id             TEXT PRIMARY KEY,
    ConversationId TEXT NOT NULL REFERENCES Conversations(Id) ON DELETE CASCADE,
    SequenceNo     INTEGER NOT NULL,
    Role           TEXT NOT NULL, -- User/Assistant/System/Tool
    Content        TEXT NOT NULL,
    Status         TEXT NOT NULL, -- Pending/Streaming/Completed/Failed/Cancelled
    ErrorCode      TEXT,
    ErrorMessage   TEXT,
    CreatedAt      TEXT NOT NULL,
    UpdatedAt      TEXT NOT NULL,
    UNIQUE (ConversationId, SequenceNo)
);

CREATE TABLE AppSettings (
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);

CREATE INDEX IX_Conversations_UpdatedAt ON Conversations(UpdatedAt DESC);
CREATE INDEX IX_Messages_Conversation_Sequence ON Messages(ConversationId, SequenceNo);
CREATE INDEX IX_Skills_SortOrder ON Skills(SortOrder);
```

Provider 与 skill 被历史会话引用时只能禁用，不得物理删除。会话可归档或物理删除；物理删除通过外键级联删除 messages 和 `SessionState`。内置 skill 不可删除。

## 6. Provider 与聊天服务

`AgentFactory` 根据 `ProviderType` 创建对应 `ChatClient` 和 `AIAgent`，再以 skill 的 system prompt 和固定配置构造 agent。每种 provider 独立实现连接测试、模型调用、错误归类与流式适配；公共调用面为：

```csharp
public interface IChatGateway
{
    Task<ProviderConnectionResult> TestConnectionAsync(ApiProvider provider, CancellationToken cancellationToken);
    Task<AIAgent> CreateAgentAsync(Conversation conversation, ApiProvider provider, Skill? skill,
        CancellationToken cancellationToken);
}
```

`ChatService` 负责：获取/恢复 session、创建 `CancellationTokenSource`、调用 `RunStreamingAsync`、按节流策略合并 UI 更新、将最终消息与序列化 session 写入数据库。流式异常、网络超时、认证失败、限流和空响应必须映射为稳定错误码；429 和短暂 5xx 使用有限次数指数退避，认证和配置错误不重试。

发送时先写入用户消息和占位助手消息。收到第一个有效增量后把助手消息设为 `Streaming`，结束后设为 `Completed` 并持久化 session。取消设为 `Cancelled`；异常设为 `Failed` 并保存不含敏感信息的错误摘要。应用异常退出后，下次启动把残留 `Pending`/`Streaming` 消息收敛为 `Cancelled`。

## 7. 窗口与交互

### MainWindow（设置，800×550）

- 提供商：列表、详情、添加/编辑、连接测试、设置默认、启用/禁用、删除；
- 技能：内置/自定义分组，新增、编辑、排序、启用/禁用、删除；
- 快捷键：显示当前组合，修改、冲突校验、恢复默认；
- 常规：启动行为、默认呼出行为、诊断日志、清除本地数据；
- 关于：版本、平台、数据库版本、开源许可证、日志目录。

### ChatWindow（400×520，最小尺寸 360×440）

- 顶栏：技能选择、模型/提供商选择、历史会话、复制最后回复、最小化、关闭；
- 消息区：Markdown、代码块复制、流式气泡、自动滚动、发送失败重试；
- 输入区：多行输入，Enter 发送、Shift+Enter 换行、发送/停止按钮；
- 历史列表：搜索、继续、重命名、归档、删除；
- 呼出规则：快捷键默认创建新会话；用户可在设置中启用“继续最近会话”。Esc 隐藏窗口，不退出应用。

### 系统集成

- Windows 使用 `RegisterHotKey`，macOS 使用系统事件监控实现全局快捷键；注册失败、权限不足或冲突必须展示可操作提示；
- macOS 全局快捷键所需的辅助功能权限必须在首次注册失败时引导用户授予；
- 托盘菜单包含显示聊天、显示设置、创建会话、退出；退出时取消活动请求、序列化全部 session、注销快捷键并关闭数据库；
- 使用单实例守护。再次启动时激活已运行实例并显示聊天窗口。

## 8. Native AOT 与发布

Native AOT 是生产交付基线。所有依赖必须通过 trimming/AOT 分析，不得依赖动态程序集加载、`Reflection.Emit`、运行时代理生成或未标注的反射序列化。MAF、Avalonia、SQLite 和 provider SDK 的 AOT 警告必须在引入依赖时消除或记录为经验证的兼容配置。

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <InvariantGlobalization>false</InvariantGlobalization>
</PropertyGroup>
```

发布矩阵：`win-x64`、`win-arm64`、`osx-x64`、`osx-arm64`。每个 RID 在对应操作系统和架构的 CI runner 上执行 restore、build、test、publish、启动冒烟测试和核心聊天回归；macOS 包必须签名、notarize 并提供 `.dSYM` 符号文件，Windows 包必须签名并保留 `.pdb`。所有发布工件生成 SBOM、校验和和版本清单。

## 9. 工程结构

```
Chater/
├── Models/                 # 数据实体、枚举、DTO
├── Data/                   # SQLite 连接、迁移、具体仓储
├── Providers/              # ChatGateway、AgentFactory、provider 适配器
├── Services/               # 聊天、会话、消息、技能、提供商、生命周期
├── Platform/
│   ├── Windows/            # 快捷键、单实例、平台路径
│   └── macOS/              # 快捷键、权限、单实例、平台路径
├── ViewModels/
├── Views/
├── Assets/
└── Tests/
    ├── Unit/
    ├── Integration/
    └── EndToEnd/
```

## 10. 迭代计划

### 迭代 1：产品基础与发布骨架

- 建立目录、DI、配置、日志、SQLite 迁移、应用数据目录、单实例和统一错误模型；
- 加入 Native AOT 项目配置及四 RID 的 CI 发布矩阵；
- 完成数据库、迁移和日志的单元/集成测试。

完成条件：四个 RID 均可 Native AOT publish 和启动；新安装应用可初始化数据库并显示设置窗口。

### 迭代 2：Provider 管理与 MAF 会话核心

- 实现四类 provider 配置、连接测试、模型选择和 SQLite 直接保存 API Key；
- 实现 `AgentFactory`、`IChatGateway`、`ConversationService` 和 MAF Session 的创建、序列化、反序列化与配置指纹校验；
- 实现会话和消息数据访问、启动恢复、异常收敛。

完成条件：创建 provider 与会话后，重启应用可恢复相同 agent 的 `AgentSession` 并继续对话。

### 迭代 3：完整聊天体验

- 实现 ChatWindow、流式消息、Markdown、代码复制、取消、重试、自动滚动和输入快捷键；
- 完成错误、超时、限流、网络中断、无可用 provider 的状态呈现；
- 实现会话搜索、重命名、归档、删除和继续最近会话。

完成条件：用户可稳定完成创建、流式多轮聊天、取消、重试、重启恢复和历史管理全链路。

### 迭代 4：技能与桌面集成

- 实现全部内置技能、自定义技能管理、排序与版本快照；
- 完成 Windows/macOS 快捷键、托盘、Esc 隐藏、权限引导和单实例激活；
- 打磨首次启动、空状态、无网络和可访问性。

完成条件：Windows 和 macOS 上的设置、呼出、聊天和退出生命周期均符合一致交互。

### 迭代 5：发布质量

- 补齐单元、数据库集成、mock provider、端到端和 AOT 冒烟测试；
- 执行性能、内存、数据库迁移、升级兼容、日志脱敏和崩溃恢复测试；
- 完成安装包、签名/notarization、版本迁移、隐私说明、帮助页面和发布清单。

完成条件：满足第 11 节发布门槛，四个发布工件通过验收。

## 11. 质量与发布门槛

- 所有支持 RID 的 Release Native AOT 发布成功，无未处置的 trimming/AOT 警告；
- Windows 与 macOS 均通过 provider 配置、连接测试、流式多轮、取消、重试、重启恢复、会话管理、技能管理、快捷键、托盘和单实例端到端测试；
- SQLite 迁移可从任意已支持版本升级，失败时可诊断且不破坏已有数据；
- API Key 不进入日志、错误报告、诊断包、导出和 UI 复制路径；
- 正常退出不丢失已完成消息和 SessionState；强制退出后不保留无终态的流式消息；
- 启动、呼出、发送、首 token、内存和数据库大小均在目标机器上采集基线并纳入回归；
- 发布包完成签名、校验、SBOM、隐私说明和升级/卸载验证。

## 12. 验收标准

1. 用户能够在 Windows 或 macOS 完成安装、配置任一支持 provider、测试连接并发送流式消息。
2. 同一会话完成多轮对话、关闭应用、重新打开后，MAF Session 可恢复并保持上下文；配置不匹配的 session 被安全拒绝恢复。
3. 发送中的取消、超时、认证失败、限流和网络失败均产生明确界面状态，且之后可以继续聊天。
4. 用户可管理全部 provider、内置/自定义技能和历史会话；数据操作符合禁用、归档、删除和外键规则。
5. 全局快捷键、托盘、Esc 隐藏、单实例与退出行为在 Windows 和 macOS 上均可用。
6. 四个 Native AOT 工件可安装、启动、运行核心聊天路径并通过发布质量门槛。
