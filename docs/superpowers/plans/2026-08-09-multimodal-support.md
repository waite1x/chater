# 多模态支持 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Chater 增加图片多模态能力：模型级“是否多模态”标记、聊天窗口添加图片按钮与缩略图预览、图片随 ChatMessage 发送并由历史持久化/重放。

**Architecture:** 模型元数据存于 SQLite（`ProviderModels.IsMultimodal`），`ApiProvider` 暴露 `MultimodalModelIds` 集合；聊天侧把用户选择图片复制到应用 `attachments/` 目录，附件元数据（副本路径/文件名/MIME）以 JSON 存于 `Messages.Attachments` 列；`ChatService` 读取图片字节构造 `Microsoft.Extensions.AI.ChatMessage`（`TextContent` + `DataContent`），经 `agent.RunStreamingAsync(ChatMessage, ...)` 发送。

**Tech Stack:** C# / .NET 10, Avalonia 11, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, Microsoft.Agents.AI.Harness, Microsoft.Extensions.AI, xUnit。

## Global Constraints

- 目标框架 `net10.0`，`IsAotCompatible=true`：新增 JSON 序列化必须使用 `Chater/AI/ChaterJsonSerializerContext.cs` 源生成上下文。
- 仓库 `Converters\**` 目录已被 `Chater.csproj` 从编译与 Avalonia 资源中排除，任何新转换器不得放在该目录（放 `Chater/Views/` 下）。
- `ChatWindowViewModel` 构造函数参数追加在末尾（现有测试按位置传参，`navigation` 为第 7 个）；新增参数必须带默认值。
- `ApiProvider.ModelIds` 保持不变（向后兼容），只新增 `MultimodalModelIds` init 属性。
- `Message` 记录主构造签名保持不变，新增 `Attachments` init 属性。
- 三个 resx 必须同步新增键：`Chater/Localization/Resources.resx`、`Resources.zh-CN.resx`、`Resources.zh-TW.resx`。
- 测试命令：在仓库根目录外运行（dotnet 需要 ~/.nuget 访问）：`DOTNET_CLI_HOME=$HOME dotnet test Chater.Tests/Chater.Tests.csproj`。
- `Microsoft.Agents.AI` 的 `AIAgent` 已提供 `RunStreamingAsync(ChatMessage, ...)` 与 `RunStreamingAsync(IEnumerable<ChatMessage>, ...)` 重载。

---

### Task 1: 数据库迁移 0003（ProviderModels.IsMultimodal + Messages.Attachments）

**Files:**
- Create: `Chater/Data/Migrations/0003_ProviderModelsMultimodal.sql`
- Modify: `Chater/Data/DatabaseMigrator.cs`（`LatestVersion` 2→3；`ReadMigrationAsync` 名称映射）
- Test: `Chater.Tests/DatabaseMigratorTests.cs`

**Interfaces:**
- Consumes: 现有 `DatabaseMigrator.MigrateAsync()`。
- Produces: `ProviderModels` 表含 `IsMultimodal INTEGER NOT NULL DEFAULT 0` 列；`Messages` 表含 `Attachments TEXT NULL` 列。后续任务的 `ApiProviderRepository`/`MessageRepository` 依赖这两列。

- [ ] **Step 1: 更新迁移测试（先失败）**

将 `DatabaseMigratorTests.MigrateAsync_CreatesSchemaAndSeedsBuiltInSkills_Idempotently` 中 `Assert.Equal(2L, ... SchemaMigrations)` 改为 `3L`，并在其后追加两列存在性断言：

```csharp
Assert.Equal(3L, await ScalarAsync(connection, "SELECT COUNT(*) FROM SchemaMigrations;"));
// ...existing asserts...
Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ProviderModels') WHERE name = 'IsMultimodal';"));
Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('Messages') WHERE name = 'Attachments';"));
```

- [ ] **Step 2: 运行测试确认失败**

Run: `DOTNET_CLI_HOME=$HOME dotnet test Chater.Tests/Chater.Tests.csproj --filter "FullyQualifiedName~DatabaseMigratorTests" -v n`
Expected: FAIL（断言 3 迁移，实际 2；且列不存在）。

- [ ] **Step 3: 新建迁移 SQL**

创建 `Chater/Data/Migrations/0003_ProviderModelsMultimodal.sql`：

```sql
ALTER TABLE ProviderModels ADD COLUMN IsMultimodal INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Messages ADD COLUMN Attachments TEXT NULL;
```

- [ ] **Step 4: 更新 DatabaseMigrator**

`LatestVersion` 常量改为 `3`，并把 `ReadMigrationAsync` 的资源名映射改为 switch：

```csharp
private const int LatestVersion = 3;
// ...existing...
private static async Task<string> ReadMigrationAsync(int version, CancellationToken cancellationToken)
{
    var name = version switch
    {
        1 => "InitialSchema",
        2 => "ProviderModels",
        3 => "ProviderModelsMultimodal",
        _ => throw new InvalidOperationException($"Unknown migration version '{version}'.")
    };
    var resource = $"Chater.Data.Migrations.{version:0000}_{name}.sql";
    await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
        ?? throw new InvalidOperationException($"Missing database migration resource '{resource}'.");
    using var reader = new StreamReader(stream);
    return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
}
```

注意：`0003_ProviderModelsMultimodal.sql` 是 `EmbeddedResource Include="Data\Migrations\*.sql"`（已存在于 csproj），无需改 csproj。

- [ ] **Step 5: 运行测试确认通过**

Run: 同 Step 2 命令。
Expected: PASS。

- [ ] **Step 6: Commit**

```bash
git add Chater/Data/Migrations/0003_ProviderModelsMultimodal.sql Chater/Data/DatabaseMigrator.cs Chater.Tests/DatabaseMigratorTests.cs
git commit -m "feat: add migration 0003 for multimodal provider models and message attachments"
```

---

### Task 2: MessageAttachment 记录 + Message.Attachments + 序列化上下文 + MessageRepository 持久化

**Files:**
- Create: `Chater/AI/Conversations/MessageAttachment.cs`
- Modify: `Chater/AI/Conversations/Message.cs`
- Modify: `Chater/AI/ChaterJsonSerializerContext.cs`
- Modify: `Chater/AI/Tools/MessageRepository.cs`
- Test: `Chater.Tests/RepositoryTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `Messages.Attachments` 列。
- Produces:
  - `Chater.AI.Conversations.MessageAttachment(string FilePath, string FileName, string MimeType)`。
  - `Message.Attachments : IReadOnlyList<MessageAttachment>`（init，默认空）。
  - `ChaterJsonSerializerContext.MessageAttachmentArray`（`JsonSerializable(typeof(MessageAttachment[]))`）。
  - `MessageRepository.AppendAsync` 写附件 JSON、`GetByConversationAsync` 读回。供 Task 6（ChatService）与 Task 7（历史渲染）使用。

- [ ] **Step 1: 写失败测试**

在 `Chater.Tests/RepositoryTests.cs` 增加一个测试：

```csharp
[Fact]
public async Task AppendAsync_RoundTripsMessageAttachments()
{
    var database = await CreateDatabaseAsync();
    var provider = CreateProvider("provider", true);
    await new ApiProviderRepository(database).SaveAsync(provider);
    var now = DateTimeOffset.UtcNow;
    var conversation = new Conversation("conversation", "Conversation", provider.Id, null, "{}", null, "agent", "hash", "1", "{}", SessionStatus.Active, false, now, now);
    await new ConversationRepository(database).SaveAsync(conversation);
    var repository = new MessageRepository(database);
    var message = new Message("m1", conversation.Id, 1, MessageRole.User, "look", MessageStatus.Completed, null, null, now, now)
    {
        Attachments = [new MessageAttachment("/tmp/a.png", "a.png", "image/png")]
    };

    await repository.AppendAsync(message);
    var history = await repository.GetByConversationAsync(conversation.Id);

    var saved = Assert.Single(history);
    Assert.Equal("look", saved.Content);
    var attachment = Assert.Single(saved.Attachments);
    Assert.Equal("/tmp/a.png", attachment.FilePath);
    Assert.Equal("a.png", attachment.FileName);
    Assert.Equal("image/png", attachment.MimeType);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `DOTNET_CLI_HOME=$HOME dotnet test Chater.Tests/Chater.Tests.csproj --filter "FullyQualifiedName~RepositoryTests" -v n`
Expected: FAIL（`MessageAttachment` 不存在；`Message.Attachments` 不存在；列不存在）。

- [ ] **Step 3: 创建 MessageAttachment 记录**

创建 `Chater/AI/Conversations/MessageAttachment.cs`：

```csharp
namespace Chater.AI.Conversations;

/// <summary>Metadata for an image attached to a user message. <see cref="FilePath"/> points to a copy stored under the app attachments directory.</summary>
public sealed record MessageAttachment(string FilePath, string FileName, string MimeType);
```

- [ ] **Step 4: Message 增加 Attachments init 属性**

在 `Chater/AI/Conversations/Message.cs` 的 record 体增加：

```csharp
public sealed record Message(
    string Id,
    string ConversationId,
    long SequenceNo,
    MessageRole Role,
    string Content,
    MessageStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Attached image metadata, persisted as JSON in the Messages.Attachments column.</summary>
    public IReadOnlyList<MessageAttachment> Attachments { get; init; } = [];
}
```

- [ ] **Step 5: 注册序列化上下文**

在 `Chater/AI/ChaterJsonSerializerContext.cs` 增加：

```csharp
using Chater.AI.Conversations;
// ...existing usings...

[JsonSerializable(typeof(ProviderSnapshot))]
[JsonSerializable(typeof(UpdateService.GitHubRelease))]
[JsonSerializable(typeof(MessageAttachment[]))]
internal sealed partial class ChaterJsonSerializerContext : JsonSerializerContext;
```

- [ ] **Step 6: MessageRepository 读写附件**

`Chater/AI/Tools/MessageRepository.cs`（命名空间 `Chater.Data`）：

顶部加 using：

```csharp
using System.Text.Json;
using Chater.AI;
using Chater.AI.Conversations;
using Chater.Models;
```

`AppendAsync` 中 INSERT 语句与参数改为（插入列 `Attachments` 与参数 `$attachments`）：

```csharp
command.CommandText = "INSERT INTO Messages (Id, ConversationId, SequenceNo, Role, Content, Status, ErrorCode, ErrorMessage, Attachments, CreatedAt, UpdatedAt) VALUES ($id, $conversationId, $sequenceNo, $role, $content, $status, $errorCode, $errorMessage, $attachments, $createdAt, $updatedAt);";
command.Parameters.AddWithValue("$id", message.Id); command.Parameters.AddWithValue("$conversationId", message.ConversationId); command.Parameters.AddWithValue("$sequenceNo", message.SequenceNo); command.Parameters.AddWithValue("$role", (int)message.Role); command.Parameters.AddWithValue("$content", message.Content); command.Parameters.AddWithValue("$status", (int)message.Status); command.Parameters.AddWithValue("$errorCode", message.ErrorCode ?? (object)DBNull.Value); command.Parameters.AddWithValue("$errorMessage", message.ErrorMessage ?? (object)DBNull.Value); command.Parameters.AddWithValue("$attachments", message.Attachments.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(message.Attachments, ChaterJsonSerializerContext.Default.MessageAttachmentArray)); command.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$updatedAt", message.UpdatedAt.ToString("O"));
```

`GetByConversationAsync` 中 SELECT 与读取改为：

```csharp
command.CommandText = "SELECT Id, ConversationId, SequenceNo, Role, Content, Status, ErrorCode, ErrorMessage, Attachments, CreatedAt, UpdatedAt FROM Messages WHERE ConversationId = $conversationId ORDER BY SequenceNo;";
// ...existing...
while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
{
    var message = new Message(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), (MessageRole)reader.GetInt32(3), reader.GetString(4), (MessageStatus)reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), DateTimeOffset.Parse(reader.GetString(8)), DateTimeOffset.Parse(reader.GetString(9)));
    if (!reader.IsDBNull(10))
    {
        message = message with
        {
            Attachments = JsonSerializer.Deserialize(reader.GetString(10), ChaterJsonSerializerContext.Default.MessageAttachmentArray) ?? []
        };
    }
    messages.Add(message);
}
```

- [ ] **Step 7: 运行测试确认通过**

Run: 同 Step 2 命令。
Expected: PASS（原有 RepositoryTests 亦应通过）。

- [ ] **Step 8: Commit**

```bash
git add Chater/AI/Conversations/MessageAttachment.cs Chater/AI/Conversations/Message.cs Chater/AI/ChaterJsonSerializerContext.cs Chater/AI/Tools/MessageRepository.cs Chater.Tests/RepositoryTests.cs
git commit -m "feat: persist message attachments metadata in Messages table"
```

---

### Task 3: ApiProvider.MultimodalModelIds + ApiProviderRepository 往返

**Files:**
- Modify: `Chater/AI/Providers/ApiProvider.cs`
- Modify: `Chater/AI/Providers/ApiProviderRepository.cs`
- Test: `Chater.Tests/RepositoryTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `ProviderModels.IsMultimodal` 列。
- Produces: `ApiProvider.MultimodalModelIds : IReadOnlySet<string>`（init，默认空）。供 Task 4（设置页保存/回填）与 Task 7（聊天窗口显示按钮）使用。

- [ ] **Step 1: 写失败测试**

在 `Chater.Tests/RepositoryTests.cs` 增加：

```csharp
[Fact]
public async Task SaveAsync_RoundTripsMultimodalModelFlags()
{
    var database = await CreateDatabaseAsync();
    var provider = CreateProvider("one", true) with
    {
        ModelIds = ["model-a", "model-b"],
        ModelId = "model-a",
        MultimodalModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "model-b" }
    };

    await new ApiProviderRepository(database).SaveAsync(provider);

    var saved = await new ApiProviderRepository(database).GetByIdAsync(provider.Id);
    Assert.Equal(["model-a", "model-b"], saved?.ModelIds);
    Assert.Contains("model-b", saved?.MultimodalModelIds ?? []);
    Assert.DoesNotContain("model-a", saved?.MultimodalModelIds ?? []);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `DOTNET_CLI_HOME=$HOME dotnet test Chater.Tests/Chater.Tests.csproj --filter "FullyQualifiedName~RepositoryTests" -v n`
Expected: FAIL（`MultimodalModelIds` 不存在）。

- [ ] **Step 3: ApiProvider 增加属性**

在 `Chater/AI/Providers/ApiProvider.cs` 的 record 体增加：

```csharp
// ModelId remains the active model for backwards compatibility. ModelIds
// contains all models that share this provider/API key.
public IReadOnlyList<string> ModelIds { get; init; } = [ModelId];
/// <summary>Model IDs (subset of <see cref="ModelIds"/>) that accept image input.</summary>
public IReadOnlySet<string> MultimodalModelIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
public string ModelSummary => string.Join(", ", ModelIds);
```

- [ ] **Step 4: ApiProviderRepository 读写标记**

`Chater/AI/Providers/ApiProviderRepository.cs`：

`SaveAsync` 中模型插入循环改为（把 `IsMultimodal` 加入 INSERT 并传参）：

```csharp
var models = provider.ModelIds.Append(provider.ModelId).Where(model => !string.IsNullOrWhiteSpace(model)).Select(model => model.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
foreach (var model in models)
{
    await ExecuteAsync(connection, transaction, "INSERT INTO ProviderModels (ProviderId, ModelId, IsMultimodal) VALUES ($providerId, $modelId, $isMultimodal);", cancellationToken, ("$providerId", provider.Id), ("$modelId", model), ("$isMultimodal", provider.MultimodalModelIds.Contains(model) ? 1 : 0)).ConfigureAwait(false);
}
```

`AddModelsAsync` 中读取改为：

```csharp
command.CommandText = "SELECT ModelId, IsMultimodal FROM ProviderModels WHERE ProviderId = $providerId ORDER BY ModelId;";
// ...existing...
var models = new List<string>();
var multimodal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
while (await modelsReader.ReadAsync(cancellationToken).ConfigureAwait(false))
{
    var model = modelsReader.GetString(0);
    models.Add(model);
    if (modelsReader.GetInt64(1) == 1) multimodal.Add(model);
}
result.Add(provider with
{
    ModelIds = models.Count == 0 ? [provider.ModelId] : models,
    MultimodalModelIds = multimodal
});
```

- [ ] **Step 5: 运行测试确认通过**

Run: 同 Step 2 命令。
Expected: PASS。

- [ ] **Step 6: Commit**

```bash
git add Chater/AI/Providers/ApiProvider.cs Chater/AI/Providers/ApiProviderRepository.cs Chater.Tests/RepositoryTests.cs
git commit -m "feat: add per-model multimodal flag to provider models"
```

---

### Task 4: 设置页模型列表拆分（ProviderModelItem + ApiKeySettingsViewModel + 视图）

**Files:**
- Create: `Chater/ViewModels/ProviderModelItem.cs`
- Create: `Chater.Tests/ApiKeySettingsViewModelTests.cs`
- Modify: `Chater/ViewModels/ApiKeySettingsViewModel.cs`
- Modify: `Chater/Views/Settings/ApiKeySettingsView.axaml`
- Modify: `Chater/Localization/Resources.resx`、`Resources.zh-CN.resx`、`Resources.zh-TW.resx`（键：`Multimodal`、`AddModel`）

**Interfaces:**
- Consumes: Task 3 的 `ApiProvider.MultimodalModelIds`。
- Produces:
  - `ProviderModelItem(string ModelId, bool IsMultimodal)`（可观察属性）。
  - `ApiKeySettingsViewModel.ProviderModels : ObservableCollection<ProviderModelItem>`、`AddModelCommand`、`RemoveModelCommand(ProviderModelItem)`、`AddFetchedModel(string?)`（追加行）。
  - 静态方法 `ApiKeySettingsViewModel.BuildModelLists(IEnumerable<ProviderModelItem>) -> (string[] ModelIds, IReadOnlySet<string> MultimodalModelIds)`，供保存与测试使用。

- [ ] **Step 1: 写失败测试**

创建 `Chater.Tests/ApiKeySettingsViewModelTests.cs`：

```csharp
using Chater.AI.Providers;
using Chater.Data;
using Chater.Localization;
using Chater.Services;
using Chater.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Chater.Tests;

public sealed class ApiKeySettingsViewModelTests
{
    [Fact]
    public void SelectedProvider_PopulatesModelRowsWithMultimodalFlags()
    {
        var viewModel = CreateViewModel();
        var provider = new ApiProvider("p", "Provider", ProviderType.OpenAi, "key", null, "model-a", true, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        {
            ModelIds = ["model-a", "model-b"],
            MultimodalModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "model-b" }
        };

        viewModel.SelectedProvider = provider;

        Assert.Equal(2, viewModel.ProviderModels.Count);
        Assert.Equal("model-a", viewModel.ProviderModels[0].ModelId);
        Assert.False(viewModel.ProviderModels[0].IsMultimodal);
        Assert.Equal("model-b", viewModel.ProviderModels[1].ModelId);
        Assert.True(viewModel.ProviderModels[1].IsMultimodal);
    }

    [Fact]
    public void BuildModelLists_MapsRowsToModelIdsAndMultimodalSet()
    {
        var rows = new[]
        {
            new ProviderModelItem("model-a", false),
            new ProviderModelItem("model-b", true),
            new ProviderModelItem("", true),
            new ProviderModelItem("model-a", true)
        };

        var (modelIds, multimodal) = ApiKeySettingsViewModel.BuildModelLists(rows);

        Assert.Equal(["model-a", "model-b"], modelIds);
        Assert.Contains("model-b", multimodal);
        Assert.Contains("model-a", multimodal);
    }

    [Fact]
    public void AddAndRemoveModel_UpdatesRows()
    {
        var viewModel = CreateViewModel();
        viewModel.ProviderModels.Add(new ProviderModelItem("model-a", false));

        viewModel.AddModelCommand.Execute(null);
        Assert.Equal(2, viewModel.ProviderModels.Count);

        viewModel.RemoveModelCommand.Execute(viewModel.ProviderModels[1]);
        Assert.Single(viewModel.ProviderModels);
    }

    private static ApiKeySettingsViewModel CreateViewModel()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var appState = new AppState(new LazyServiceProvider(services));
        var repository = new ApiProviderRepository(new SqliteDatabase(Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.db")));
        return new ApiKeySettingsViewModel(new ProviderService(repository), appState, new LocalizationService());
    }
}
```

注意：`BuildModelLists` 行为 —— 空 `ModelId` 行被忽略；只要某模型存在勾选行（`IsMultimodal=true` 且 `ModelId` 非空）即进入多模态集合；`modelIds` 去重保序。

- [ ] **Step 2: 运行测试确认失败**

Run: `DOTNET_CLI_HOME=$HOME dotnet test Chater.Tests/Chater.Tests.csproj --filter "FullyQualifiedName~ApiKeySettingsViewModelTests" -v n`
Expected: FAIL（`ProviderModelItem`、`ProviderModels`、`BuildModelLists` 不存在）。

- [ ] **Step 3: 创建 ProviderModelItem**

创建 `Chater/ViewModels/ProviderModelItem.cs`：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chater.ViewModels;

public sealed partial class ProviderModelItem : ViewModelBase
{
    public ProviderModelItem(string modelId = "", bool isMultimodal = false)
    {
        ModelId = modelId;
        IsMultimodal = isMultimodal;
    }

    [ObservableProperty] private string _modelId;
    [ObservableProperty] private bool _isMultimodal;
}
```

- [ ] **Step 4: 扩展 ApiKeySettingsViewModel**

`Chater/ViewModels/ApiKeySettingsViewModel.cs`：

- 新增集合与命令：

```csharp
public ObservableCollection<ProviderModelItem> ProviderModels { get; } = [];

[RelayCommand]
private void AddModel() => ProviderModels.Add(new ProviderModelItem());

[RelayCommand]
private void RemoveModel(ProviderModelItem? item)
{
    if (item is not null) ProviderModels.Remove(item);
}
```

- 删除 `[ObservableProperty] private string _providerModelId = string.Empty;` 及其在 `AddProviderCommand`、`OnSelectedProviderChanged` 中的赋值。
- 新增静态映射方法：

```csharp
/// <summary>Projects editable model rows into the model ID list and the multimodal model ID set.</summary>
public static (string[] ModelIds, IReadOnlySet<string> MultimodalModelIds) BuildModelLists(IEnumerable<ProviderModelItem> items)
{
    var rows = items
        .Select(item => (ModelId: item.ModelId.Trim(), item.IsMultimodal))
        .Where(row => row.ModelId.Length > 0)
        .ToList();
    var modelIds = rows.Select(row => row.ModelId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    var multimodal = rows
        .Where(row => row.IsMultimodal)
        .Select(row => row.ModelId)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    return (modelIds, multimodal);
}
```

- `AddFetchedModel` 改为追加行（去重按 ModelId 不区分大小写）：

```csharp
[RelayCommand]
private void AddFetchedModel(string? modelId)
{
    if (string.IsNullOrWhiteSpace(modelId)) return;
    var trimmed = modelId.Trim();
    if (ProviderModels.Any(item => string.Equals(item.ModelId, trimmed, StringComparison.OrdinalIgnoreCase))) return;
    ProviderModels.Add(new ProviderModelItem(trimmed, false));
}
```

- `AddProviderCommand` 中把 `ProviderModelId = string.Empty;` 替换为 `ProviderModels.Clear(); ProviderModels.Add(new ProviderModelItem());`。
- `OnSelectedProviderChanged` 中把 `ProviderModelId = string.Join(Environment.NewLine, value.ModelIds);` 替换为回填行：

```csharp
ProviderModels.Clear();
foreach (var model in value.ModelIds)
{
    ProviderModels.Add(new ProviderModelItem(model, value.MultimodalModelIds.Contains(model, StringComparer.OrdinalIgnoreCase)));
}
```

- `BuildEditedProvider()` 中把 `modelIds` 的计算替换为调用静态方法：

```csharp
private ApiProvider BuildEditedProvider()
{
    var existing = SelectedProvider;
    var now = DateTimeOffset.UtcNow;
    var (modelIds, multimodalModelIds) = BuildModelLists(ProviderModels);
    var activeModel = modelIds.FirstOrDefault() ?? string.Empty;
    return new ApiProvider(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(ProviderName) ? (existing?.Name ?? "Unnamed") : ProviderName.Trim(),
            ProviderType,
            string.IsNullOrWhiteSpace(ProviderApiKey) ? existing?.ApiKey ?? string.Empty : ProviderApiKey,
            string.IsNullOrWhiteSpace(ProviderEndpoint) ? null : ProviderEndpoint.Trim(),
            activeModel,
            existing?.IsDefault ?? Providers.Count == 0,
            true,
            existing?.CreatedAt ?? now,
            now) with
        {
            ModelIds = modelIds,
            MultimodalModelIds = multimodalModelIds
        };
}
```

（删除原 `var modelIds = ProviderModelId.Split(...)` 逻辑。）

- [ ] **Step 5: 更新视图**

`Chater/Views/Settings/ApiKeySettingsView.axaml`：把“4. Model List”中的多行 `TextBox`（`Text="{Binding ProviderModelId}" ... AcceptsReturn`）整块替换为模型行列表 + 添加按钮：

```xml
<!-- Manual model list: one row per model with multimodal flag -->
<ItemsControl ItemsSource="{Binding ProviderModels}">
    <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="vm:ProviderModelItem">
            <Grid ColumnDefinitions="*,Auto,Auto" ColumnSpacing="8" Margin="0,0,0,6">
                <TextBox Text="{Binding ModelId, Mode=TwoWay}"
                         PlaceholderText="{Binding $parent[UserControl].DataContext.Localization[ModelListPlaceholder]}"/>
                <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="4" VerticalAlignment="Center">
                    <CheckBox IsChecked="{Binding IsMultimodal, Mode=TwoWay}"
                              Content="{Binding $parent[UserControl].DataContext.Localization[Multimodal]}"/>
                </StackPanel>
                <Button Grid.Column="2" Classes="transparent-button" Padding="4"
                        Command="{Binding $parent[UserControl].DataContext.RemoveModelCommand}"
                        CommandParameter="{Binding}"
                        ToolTip.Tip="{Binding $parent[UserControl].DataContext.Localization[RemoveAttachment]}">
                    <materialIcons:MaterialIcon Kind="Close" Width="14" Height="14"/>
                </Button>
            </Grid>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
<Button Classes="transparent-button" HorizontalAlignment="Left" Padding="8,4"
        Command="{Binding AddModelCommand}">
    <StackPanel Orientation="Horizontal" Spacing="4">
        <materialIcons:MaterialIcon Kind="Plus" Width="14" Height="14"/>
        <TextBlock Text="{Binding Localization[AddModel]}"/>
    </StackPanel>
</Button>
```

- [ ] **Step 6: 本地化新增键（3 个 resx）**

在 `Resources.resx`（英文）追加：

```xml
<data name="Multimodal">
    <value>Multimodal</value>
</data>
<data name="AddModel">
    <value>Add model</value>
</data>
```

在 `Resources.zh-CN.resx` 追加：

```xml
<data name="Multimodal">
    <value>多模态</value>
</data>
<data name="AddModel">
    <value>添加模型</value>
</data>
```

在 `Resources.zh-TW.resx` 追加：

```xml
<data name="Multimodal">
    <value>多模態</value>
</data>
<data name="AddModel">
    <value>新增模型</value>
</data>
```

- [ ] **Step 7: 运行测试确认通过**

Run: `DOTNET_CLI_HOME=$HOME dotnet test Chater.Tests/Chater.Tests.csproj --filter "FullyQualifiedName~ApiKeySettingsViewModelTests|FullyQualifiedName~RepositoryTests" -v n`
Expected: PASS。

- [ ] **Step 8: Commit**

```bash
git add Chater/ViewModels/ProviderModelItem.cs Chater/ViewModels/ApiKeySettingsViewModel.cs Chater/Views/Settings/ApiKeySettingsView.axaml Chater/Localization/Resources.resx Chater/Localization/Resources.zh-CN.resx Chater/Localization/Resources.zh-TW.resx Chater.Tests/ApiKeySettingsViewModelTests.cs
git commit -m "feat: split provider model list into rows with per-model multimodal flag"
```

---

### Task 5: AppPaths 附件目录

**Files:**
- Modify: `Chater/Services/AppPaths.cs`

**Interfaces:**
- Consumes: 无。
- Produces: `AppPaths.AttachmentsDirectory : string`（`<ApplicationDataDirectory>/attachments`），并在 `EnsureCreated()` 中创建。供 Task 7 复制图片使用。

- [ ] **Step 1: 实现**

在 `Chater/Services/AppPaths.cs` 增加：

```csharp
public string AttachmentsDirectory => Path.Combine(ApplicationDataDirectory, "attachments");
```

并把 `EnsureCreated()` 增加一行：

```csharp
public void EnsureCreated()
{
    Directory.CreateDirectory(ApplicationDataDirectory);
    Directory.CreateDirectory(LogsDirectory);
    Directory.CreateDirectory(ExportsDirectory);
    Directory.CreateDirectory(AttachmentsDirectory);
}
```

- [ ] **Step 2: 构建确认**

Run: `DOTNET_CLI_HOME=$HOME dotnet build Chater/Chater.csproj -v q`
Expected: 成功。

- [ ] **Step 3: Commit**

```bash
git add Chater/Services/AppPaths.cs
git commit -m "feat: add attachments directory to AppPaths"
```

---

### Task 7: 聊天窗口附件（AttachmentViewModel + ChatWindowViewModel + 渲染）

**Files:**
- Create: `Chater/ViewModels/AttachmentViewModel.cs`
- Create: `Chater/Views/ImagePathToBitmapConverter.cs`
- Modify: `Chater/ViewModels/ChatWindowViewModel.cs`
- Modify: `Chater/ViewModels/ChatMessageViewModel.cs`
- Modify: `Chater/Views/ChatWindow.axaml`
- Modify: `Chater/Views/ChatWindow.axaml.cs`
- Modify: `Chater/Views/ConversationMessagesView.axaml`
- Modify: `Chater/Localization/Resources.resx`、`Resources.zh-CN.resx`、`Resources.zh-TW.resx`（键：`AddImage`、`RemoveAttachment`、`ImageOnlyTitle`）
- Test: `Chater.Tests/ChatWindowViewModelTests.cs`

**Interfaces:**
- Consumes: Task 3 `ApiProvider.MultimodalModelIds`；Task 5 `AppPaths.AttachmentsDirectory`；Task 2 `MessageAttachment`/`Message.Attachments`；Task 6 `ChatService.SendStreamingAsync` 新签名（含 `attachments` 参数）。
- Produces:
  - `AttachmentViewModel(string FilePath, string FileName, string MimeType)`（`IsPersisted` 可观察）。
  - `ChatWindowViewModel.Attachments : ObservableCollection<AttachmentViewModel>`、`AttachStorageProvider(IStorageProvider?)`、`AddFilesCommand`、`AddAttachmentsAsync(IEnumerable<string>)`、`RemoveAttachmentCommand`、`ShowAddAttachmentButton`。
  - `ChatMessageViewModel` 第三参 `IReadOnlyList<MessageAttachment>? attachments = null`。
  - `ImagePathToBitmapConverter.Instance`（`IValueConverter`，路径→`Bitmap`）。

- [ ] **Step 1: 写失败测试**

在 `Chater.Tests/ChatWindowViewModelTests.cs` 追加（沿用现有 `CreateViewModel`，新增可选 `AppPaths` 参数，见 Step 5）：

```csharp
[Fact]
public async Task ShowAddAttachmentButton_TracksSelectedModelMultimodalFlag()
{
    var database = new SqliteDatabase(_path);
    await new DatabaseMigrator(database).MigrateAsync();
    var viewModel = CreateViewModel(database);
    var provider = CreateProvider(modelIds: ["model-a", "model-b"]) with
    {
        MultimodalModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "model-b" }
    };
    viewModel.AppState.Providers.Add(provider);

    viewModel.PrepareNewSession();

    Assert.False(viewModel.ShowAddAttachmentButton);
    viewModel.SelectedModelId = "model-b";
    Assert.True(viewModel.ShowAddAttachmentButton);
    viewModel.SelectedModelId = "model-a";
    Assert.False(viewModel.ShowAddAttachmentButton);
}

[Fact]
public async Task AddAttachmentsAsync_CopiesFilesAndExposesThumbnails()
{
    var database = new SqliteDatabase(_path);
    await new DatabaseMigrator(database).MigrateAsync();
    var attachmentsRoot = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}");
    var viewModel = CreateViewModel(database, appPaths: new AppPaths(attachmentsRoot));
    var source = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.png");
    File.WriteAllBytes(source, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

    await viewModel.AddAttachmentsAsync([source]);

    var attachment = Assert.Single(viewModel.Attachments);
    Assert.Equal("image/png", attachment.MimeType);
    Assert.Equal(Path.GetFileName(source), attachment.FileName);
    Assert.True(File.Exists(attachment.FilePath));
    Assert.StartsWith(new AppPaths(attachmentsRoot).AttachmentsDirectory, attachment.FilePath);
    File.Delete(source);
}

[Fact]
public async Task RemoveAttachment_DeletesUnsentCopiedFile()
{
    var database = new SqliteDatabase(_path);
    await new DatabaseMigrator(database).MigrateAsync();
    var attachmentsRoot = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}");
    var viewModel = CreateViewModel(database, appPaths: new AppPaths(attachmentsRoot));
    var source = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.jpg");
    File.WriteAllBytes(source, [0xFF, 0xD8, 0xFF]);
    await viewModel.AddAttachmentsAsync([source]);

    viewModel.RemoveAttachmentCommand.Execute(viewModel.Attachments[0]);

    Assert.Empty(viewModel.Attachments);
    Assert.DoesNotContain(Directory.GetFiles(new AppPaths(attachmentsRoot).AttachmentsDirectory), static f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));
    File.Delete(source);
}

[Fact]
public async Task SwitchingToNonMultimodalModel_ClearsAttachments()
{
    var database = new SqliteDatabase(_path);
    await new DatabaseMigrator(database).MigrateAsync();
    var viewModel = CreateViewModel(database);
    var provider = CreateProvider(modelIds: ["model-a", "model-b"]) with
    {
        MultimodalModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "model-b" }
    };
    viewModel.AppState.Providers.Add(provider);
    viewModel.PrepareNewSession();
    viewModel.SelectedModelId = "model-b";
    await viewModel.AddAttachmentsAsync([CreateTempImage(out _)]);

    viewModel.SelectedModelId = "model-a";

    Assert.Empty(viewModel.Attachments);
}

[Fact]
public async Task CanSend_TrueWithOnlyAttachments()
{
    var database = new SqliteDatabase(_path);
    await new DatabaseMigrator(database).MigrateAsync();
    var viewModel = CreateViewModel(database);
    viewModel.AppState.Providers.Add(CreateProvider(modelIds: ["model-b"]) with
    {
        MultimodalModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "model-b" }
    });
    viewModel.PrepareNewSession();
    viewModel.SelectedModelId = "model-b";
    await viewModel.AddAttachmentsAsync([CreateTempImage(out _)]);

    Assert.True(viewModel.SendCommand.CanExecute(null));
}
```

并新增辅助方法（与现有 `CreateProvider` 并列）：

```csharp
private static string CreateTempImage(out string path)
{
    path = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.png");
    File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    return path;
}
```

（`SendCommand.CanExecute` 依赖 `CanSend()` 的新实现；`CreateViewModel` 需能注入 `AppPaths`，见 Step 5。）

- [ ] **Step 2: 运行测试确认失败**

Run: `DOTNET_CLI_HOME=$HOME dotnet test Chater.Tests/Chater.Tests.csproj --filter "FullyQualifiedName~ChatWindowViewModelTests" -v n`
Expected: FAIL（`ShowAddAttachmentButton`、`AddAttachmentsAsync`、`RemoveAttachmentCommand`、`AttachmentViewModel` 不存在）。

- [ ] **Step 3: 创建 AttachmentViewModel**

创建 `Chater/ViewModels/AttachmentViewModel.cs`：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chater.ViewModels;

public sealed partial class AttachmentViewModel : ViewModelBase
{
    public AttachmentViewModel(string filePath, string fileName, string mimeType)
    {
        FilePath = filePath;
        FileName = fileName;
        MimeType = mimeType;
    }

    /// <summary>Absolute path of the copied file under the app attachments directory.</summary>
    public string FilePath { get; }

    /// <summary>Original file name, for display.</summary>
    public string FileName { get; }

    public string MimeType { get; }

    /// <summary>True once the attachment has been persisted with a sent message; only unsent copies may be deleted.</summary>
    [ObservableProperty] private bool _isPersisted;
}
```

- [ ] **Step 4: 创建缩略图转换器**

创建 `Chater/Views/ImagePathToBitmapConverter.cs`：

```csharp
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Chater.Views;

public sealed class ImagePathToBitmapConverter : IValueConverter
{
    public static ImagePathToBitmapConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string path && File.Exists(path) ? new Bitmap(path) : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 5: 扩展 ChatWindowViewModel**

`Chater/ViewModels/ChatWindowViewModel.cs`：

- 新增 using：`using Avalonia.Platform.Storage;`
- 字段：

```csharp
private readonly AppPaths _appPaths;
private IStorageProvider? _storageProvider;
```

- 构造参数末尾追加 `AppPaths? appPaths = null`，并赋值：

```csharp
// ...existing constructor params...
LocalizationService? localization = null,
AppPaths? appPaths = null
)
{
    // ...existing body...
    _appPaths = appPaths ?? AppPaths.CreateDefault();
    Attachments.CollectionChanged += OnAttachmentsChanged;
}
```

- 新增集合与派生属性：

```csharp
public ObservableCollection<AttachmentViewModel> Attachments { get; } = [];

public bool ShowAddAttachmentButton =>
    SelectedProvider is not null && SelectedModelId is not null && SelectedProvider.MultimodalModelIds.Contains(SelectedModelId);
```

- 新增存储提供者挂接（供 ChatWindow 代码后台调用）：

```csharp
public void AttachStorageProvider(IStorageProvider? storageProvider) => _storageProvider = storageProvider;
```

- 新增命令与核心方法（放在 Chat actions 区）：

```csharp
[RelayCommand]
private async Task AddFilesAsync()
{
    if (_storageProvider is null) return;
    var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
    {
        Title = T("AddImage"),
        AllowMultiple = true,
        FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp"] }]
    });
    var paths = files.Select(file => file.TryGetLocalPath()).Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>().ToList();
    if (paths.Count > 0) await AddAttachmentsAsync(paths);
}

/// <summary>Copies image files into the app attachments directory and exposes them as attachments.</summary>
public async Task AddAttachmentsAsync(IEnumerable<string> sourcePaths)
{
    foreach (var source in sourcePaths)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) continue;
        var mimeType = ImageMimeTypeFromExtension(Path.GetExtension(source));
        if (mimeType is null) continue;
        var destination = Path.Combine(_appPaths.AttachmentsDirectory, $"{Guid.NewGuid():N}{Path.GetExtension(source).ToLowerInvariant()}");
        Directory.CreateDirectory(_appPaths.AttachmentsDirectory);
        File.Copy(source, destination, overwrite: false);
        Attachments.Add(new AttachmentViewModel(destination, Path.GetFileName(source), mimeType));
    }
}

[RelayCommand]
private void RemoveAttachment(AttachmentViewModel? attachment)
{
    if (attachment is null || !Attachments.Remove(attachment)) return;
    if (!attachment.IsPersisted) TryDeleteFile(attachment.FilePath);
}

private static string? ImageMimeTypeFromExtension(string extension) => extension.ToLowerInvariant() switch
{
    ".png" => "image/png",
    ".jpg" or ".jpeg" => "image/jpeg",
    ".gif" => "image/gif",
    ".webp" => "image/webp",
    ".bmp" => "image/bmp",
    _ => null
};

private static void TryDeleteFile(string path)
{
    try { if (File.Exists(path)) File.Delete(path); }
    catch { /* best-effort cleanup */ }
}

private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
{
    SendCommand.NotifyCanExecuteChanged();
    SendOrStopCommand.NotifyCanExecuteChanged();
    OnPropertyChanged(nameof(ShowAddAttachmentButton));
}

private void ClearUnsentAttachments()
{
    foreach (var attachment in Attachments.ToList())
    {
        Attachments.Remove(attachment);
        if (!attachment.IsPersisted) TryDeleteFile(attachment.FilePath);
    }
}
```

（`NotifyCollectionChangedEventArgs` 需 `using System.Collections.Specialized;`，已在文件顶部。）

- `CanSend()` 改为：

```csharp
private bool CanSend() => !IsSending && (!string.IsNullOrWhiteSpace(Draft) || Attachments.Count > 0);
```

- `OnSelectedProviderChanged` 与 `OnSelectedModelIdChanged` 末尾追加：

```csharp
OnPropertyChanged(nameof(ShowAddAttachmentButton));
if (!ShowAddAttachmentButton) ClearUnsentAttachments();
```

- `SendAsync()` 中构造附件列表并传入 ChatService、发送后清理。把：

```csharp
var text = Draft.Trim();
if (text.Length == 0) return;
```

改为：

```csharp
var text = Draft.Trim();
if (text.Length == 0 && Attachments.Count == 0) return;
```

并新增（在 `_conversation ??= await ...` 之后、`Draft = string.Empty;` 处同时处理附件）：

```csharp
var attachments = Attachments.Select(a => new MessageAttachment(a.FilePath, a.FileName, a.MimeType)).ToList();
foreach (var a in Attachments) a.IsPersisted = true;
Draft = string.Empty;
```

调用改为：

```csharp
await foreach (var update in _chat.SendStreamingAsync(_conversation.Id, text, attachments, _sendCancellation.Token).ConfigureAwait(false))
```

用户气泡改为携带附件：

```csharp
Messages.Add(new ChatMessageViewModel(MessageRole.User, text, attachments));
```

在 `SendAsync` 的 `finally` 块（`IsSending = false;` 处）追加 `Attachments.Clear();`。

- [ ] **Step 6: 扩展 ChatMessageViewModel**

`Chater/ViewModels/ChatMessageViewModel.cs` 改为：

```csharp
using Chater.AI.Conversations;

namespace Chater.ViewModels;

public sealed partial class ChatMessageViewModel(MessageRole role, string content, IReadOnlyList<MessageAttachment>? attachments = null) : ViewModelBase
{
    public MessageRole Role { get; } = role;
    public bool IsUser => Role == MessageRole.User;
    public bool IsAssistant => Role == MessageRole.Assistant;
    public IReadOnlyList<MessageAttachment> Attachments { get; } = attachments ?? [];

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _content = content;
}
```

在 `ChatWindowViewModel.OpenConversationAsync` 中把：

```csharp
Messages.Add(new ChatMessageViewModel(message.Role, message.Content));
```

改为：

```csharp
Messages.Add(new ChatMessageViewModel(message.Role, message.Content, message.Attachments));
```

- [ ] **Step 7: 更新 ChatWindow 视图**

`Chater/Views/ChatWindow.axaml`：

(a) 提示词（技能）下拉框旁、其 `ComboBox` 之后（仍 Dock=Left）新增按钮：

```xml
<Button DockPanel.Dock="Left" Classes="transparent-button" Padding="6,2"
        Command="{Binding AddFilesCommand}"
        IsVisible="{Binding ShowAddAttachmentButton}"
        ToolTip.Tip="{Binding Localization[AddImage]}"
        AutomationProperties.Name="{Binding Localization[AddImage]}">
    <materialIcons:MaterialIcon Kind="ImageMultiple" Width="16" Height="16"/>
</Button>
```

(b) 把输入区 `Grid`（含 `DraftTextBox` 与发送按钮）包进一个 `StackPanel`，其上方放附件预览条：

```xml
<StackPanel DockPanel.Dock="Bottom" Spacing="6">
    <ItemsControl ItemsSource="{Binding Attachments}"
                  IsVisible="{Binding Attachments.Count, Converter={x:Static ObjectConverters.IsNotNull}}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <WrapPanel/>
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="vm:AttachmentViewModel">
                <Border Background="{DynamicResource SystemControlBackgroundChromeMediumLowBrush}"
                        CornerRadius="6" Padding="4" Margin="0,0,6,0">
                    <StackPanel Orientation="Horizontal" Spacing="4">
                        <Image Source="{Binding FilePath, Converter={x:Static local:ImagePathToBitmapConverter.Instance}}"
                               Width="40" Height="40" Stretch="Uniform"/>
                        <Button Classes="transparent-button" Padding="2" VerticalAlignment="Center"
                                Command="{Binding $parent[Window].DataContext.RemoveAttachmentCommand}"
                                CommandParameter="{Binding}"
                                ToolTip.Tip="{Binding $parent[Window].DataContext.Localization[RemoveAttachment]}">
                            <materialIcons:MaterialIcon Kind="Close" Width="12" Height="12"/>
                        </Button>
                    </StackPanel>
                </Border>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
    <Grid ColumnDefinitions="*,Auto" ColumnSpacing="8">
        <!-- ...existing TextBox + SendOrStop Button... -->
    </Grid>
</StackPanel>
```

- [ ] **Step 8: 更新 ChatWindow 代码后台**

`Chater/Views/ChatWindow.axaml.cs` 构造方法内、`DataContext = viewModel;` 之后加：

```csharp
viewModel.AttachStorageProvider(StorageProvider);
```

- [ ] **Step 9: 历史消息渲染附件**

`Chater/Views/ConversationMessagesView.axaml`：顶部加 `xmlns:conversations="clr-namespace:Chater.AI.Conversations"`；在用户气泡的 `MarkdownView` 之前插入：

```xml
<StackPanel Spacing="4" Margin="0,0,0,6"
            IsVisible="{Binding Attachments.Count, Converter={x:Static ObjectConverters.IsNotNull}}">
    <ItemsControl ItemsSource="{Binding Attachments}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <WrapPanel/>
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="conversations:MessageAttachment">
                <Border CornerRadius="6" Margin="0,0,6,0"
                        Background="{DynamicResource SystemControlBackgroundChromeMediumLowBrush}">
                    <Image Source="{Binding FilePath, Converter={x:Static local:ImagePathToBitmapConverter.Instance}}"
                           Width="96" Height="96" Stretch="Uniform"/>
                </Border>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>
```

- [ ] **Step 10: 本地化新增键（3 个 resx）**

`Resources.resx`（英文）：

```xml
<data name="AddImage">
    <value>Add image</value>
</data>
<data name="RemoveAttachment">
    <value>Remove</value>
</data>
<data name="ImageOnlyTitle">
    <value>[Image]</value>
</data>
```

`Resources.zh-CN.resx`：

```xml
<data name="AddImage">
    <value>添加图片</value>
</data>
<data name="RemoveAttachment">
    <value>移除</value>
</data>
<data name="ImageOnlyTitle">
    <value>[图片]</value>
</data>
```

`Resources.zh-TW.resx`：

```xml
<data name="AddImage">
    <value>新增圖片</value>
</data>
<data name="RemoveAttachment">
    <value>移除</value>
</data>
<data name="ImageOnlyTitle">
    <value>[圖片]</value>
</data>
```

- [ ] **Step 11: 更新测试辅助方法并运行测试**

把 `ChatWindowViewModelTests.CreateViewModel` 签名改为（追加可选 `appPaths`）：

```csharp
private static ChatWindowViewModel CreateViewModel(SqliteDatabase database, IWindowNavigationService? navigation = null, AppPaths? appPaths = null)
{
    using var services = new ServiceCollection().BuildServiceProvider();
    var appState = new AppState(new LazyServiceProvider(services));
    var chatWindowManager = new ChatWindowManager(services.GetRequiredService<IServiceScopeFactory>(), appState);
    return new ChatWindowViewModel(
        new ConversationService(new ConversationRepository(database)),
        new ChatService(new MessageRepository(database), new ConversationRepository(database), new ApiProviderRepository(database), new SessionRunLock(), new ChatToolRegistry([])),
        new ConversationRepository(database),
        new MessageRepository(database),
        appState,
        chatWindowManager,
        navigation,
        appPaths: appPaths ?? new AppPaths(Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}")));
}
```

Run: `DOTNET_CLI_HOME=$HOME dotnet test Chater.Tests/Chater.Tests.csproj --filter "FullyQualifiedName~ChatWindowViewModelTests" -v n`
Expected: PASS。

注意：`ChatWindowViewModel` 构造要求 `AppPaths` 位于 `using Chater.Services;` 命名空间（测试已引入）。

- [ ] **Step 12: Commit**

```bash
git add Chater/ViewModels/AttachmentViewModel.cs Chater/Views/ImagePathToBitmapConverter.cs Chater/ViewModels/ChatWindowViewModel.cs Chater/ViewModels/ChatMessageViewModel.cs Chater/Views/ChatWindow.axaml Chater/Views/ChatWindow.axaml.cs Chater/Views/ConversationMessagesView.axaml Chater/Localization/Resources.resx Chater/Localization/Resources.zh-CN.resx Chater/Localization/Resources.zh-TW.resx Chater.Tests/ChatWindowViewModelTests.cs
git commit -m "feat: add image attachments to chat window with multimodal model gating"
```

---

### Task 6: ChatService 多模态发送

**Files:**
- Modify: `Chater/AI/ChatService.cs`
- Test: `Chater.Tests/ChatServiceTests.cs`

**Interfaces:**
- Consumes: Task 2 `MessageAttachment`/`Message.Attachments`。
- Produces:
  - `ChatService.SendStreamingAsync(string conversationId, string message, IReadOnlyList<MessageAttachment>? attachments = null, CancellationToken cancellationToken = default)`（现有双参调用仍兼容；Task 7 的 `ChatWindowViewModel.SendAsync` 调用点依赖此签名）。
  - `public static ChatMessage BuildUserMessage(string text, IReadOnlyList<MessageAttachment>? attachments)`。

- [ ] **Step 1: 写失败测试**

在 `Chater.Tests/ChatServiceTests.cs` 增加两个测试：

```csharp
[Fact]
public void BuildUserMessage_IncludesTextAndDataContent()
{
    var imagePath = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.png");
    File.WriteAllBytes(imagePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    var attachments = new[] { new MessageAttachment(imagePath, "a.png", "image/png") };

    var message = ChatService.BuildUserMessage("look", attachments);

    Assert.Equal(Microsoft.Extensions.AI.ChatRole.User, message.Role);
    Assert.Equal("look", message.Text);
    var data = Assert.Single(message.Contents.OfType<Microsoft.Extensions.AI.DataContent>());
    Assert.Equal("image/png", data.MediaType);
    Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], data.Data.ToArray());
    File.Delete(imagePath);
}

[Fact]
public async Task SendStreamingAsync_PersistsUserMessageWithAttachments_BeforeProviderRejection()
{
    var database = new SqliteDatabase(_path);
    await new DatabaseMigrator(database).MigrateAsync();
    await SeedConversationAsync(database);
    var service = new ChatService(new MessageRepository(database), new ConversationRepository(database), new ApiProviderRepository(database), new SessionRunLock(), new ChatToolRegistry([]));
    var imagePath = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.png");
    File.WriteAllBytes(imagePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    var attachments = new[] { new MessageAttachment(imagePath, "a.png", "image/png") };

    await Assert.ThrowsAsync<NotSupportedException>(async () =>
    {
        await foreach (var _ in service.SendStreamingAsync("conversation", "look", attachments))
        {
        }
    });

    var messages = await new MessageRepository(database).GetByConversationAsync("conversation");
    var user = Assert.Single(messages, static m => m.Role == MessageRole.User);
    Assert.Equal("look", user.Content);
    var attachment = Assert.Single(user.Attachments);
    Assert.Equal("image/png", attachment.MimeType);
    File.Delete(imagePath);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `DOTNET_CLI_HOME=$HOME dotnet test Chater.Tests/Chater.Tests.csproj --filter "FullyQualifiedName~ChatServiceTests" -v n`
Expected: FAIL（`BuildUserMessage` 不存在；或附件未持久化）。

- [ ] **Step 3: 实现 ChatService 修改**

`Chater/AI/ChatService.cs`：

- 新增常量与静态构建方法：

```csharp
private const string ImageOnlyTitle = "[Image]";

/// <summary>Builds a user <see cref="ChatMessage"/> from plain text and optional image attachments.</summary>
public static ChatMessage BuildUserMessage(string text, IReadOnlyList<MessageAttachment>? attachments)
{
    var contents = new List<AIContent> { new TextContent(text) };
    if (attachments is not null)
    {
        foreach (var attachment in attachments)
        {
            contents.Add(new DataContent(File.ReadAllBytes(attachment.FilePath), attachment.MimeType));
        }
    }

    return new ChatMessage(ChatRole.User, contents);
}
```

- 方法签名改为：

```csharp
public async IAsyncEnumerable<string> SendStreamingAsync(string conversationId, string message, IReadOnlyList<MessageAttachment>? attachments = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
```

- 标题回退（原 `ConversationService.CreateTitle(message)`）：

```csharp
var titleText = string.IsNullOrWhiteSpace(message) ? ImageOnlyTitle : message;
if (userSequenceNo == 1)
{
    conversation = conversation with { Title = ConversationService.CreateTitle(titleText), UpdatedAt = now };
    await conversations.SaveAsync(conversation, cancellationToken).ConfigureAwait(false);
}
```

- 用户消息持久化带上附件：

```csharp
await messages.AppendAsync(new Message(Guid.NewGuid().ToString("N"), conversationId, userSequenceNo, MessageRole.User, message, MessageStatus.Completed, null, null, now, now)
{
    Attachments = attachments ?? []
}, cancellationToken).ConfigureAwait(false);
```

- 运行处改为传 ChatMessage：

```csharp
var chatMessage = BuildUserMessage(message, attachments);
await using var updates = agent.RunStreamingAsync(chatMessage, session, cancellationToken: cancellationToken).GetAsyncEnumerator(cancellationToken);
```

（新增 using：`Microsoft.Extensions.AI` 已存在；`System.IO` 通过隐式 using 可用，`File`/`List` 无需额外引入。）

- [ ] **Step 4: 运行测试确认通过**

Run: 同 Step 2 命令。
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add Chater/AI/ChatService.cs Chater.Tests/ChatServiceTests.cs
git commit -m "feat: send multimodal chat messages with image data content"
```

---

### Task 8: 全量回归

**Files:**
- Test: 全部 `Chater.Tests`

- [ ] **Step 1: 运行完整测试套件**

Run: `DOTNET_CLI_HOME=$HOME dotnet test Chater.Tests/Chater.Tests.csproj -v n`
Expected: 全部通过（原 42 项 + 新增测试）。

- [ ] **Step 2: 构建主工程（含 Avalonia XAML 编译校验）**

Run: `DOTNET_CLI_HOME=$HOME dotnet build Chater/Chater.csproj -v q`
Expected: 成功（无 XAML 绑定编译错误）。

- [ ] **Step 3: 处理发现问题**

若出现编译/测试失败，按 `systematic-debugging` 处理并修复后重跑 Step 1–2。

- [ ] **Step 4: Commit（如有修复）**

```bash
git add -A
git commit -m "fix: resolve regression issues in multimodal support"
```

---

## Self-Review

**Spec coverage：**
- 需求 1（聊天窗口添加图片按钮、多图片、构建多模态内容）→ Task 7（按钮/预览/发送）、Task 6（ChatMessage+DataContent）。
- 需求 2（设置页拆分模型列表 + 每模型多模态选项；仅多模态模型显示添加图片按钮）→ Task 3（MultimodalModelIds）、Task 4（列表拆分 UI）、Task 7（`ShowAddAttachmentButton` 门控）。
- 规格其余部分：迁移 0003（Task 1）、附件持久化（Task 2）、AppPaths（Task 5）、历史缩略图（Task 7 Step 9）、本地化（Task 4/7）、测试（各 Task）。

**Placeholder scan：** 所有代码步骤均给出完整可编译代码；无 “TBD/TODO/implement later”。

**Type consistency：**
- `MessageAttachment(string FilePath, string FileName, string MimeType)` 在 Task 2/6/7 一致。
- `ChatService.SendStreamingAsync(string conversationId, string message, IReadOnlyList<MessageAttachment>? attachments = null, CancellationToken ...)` 在 Task 7 Step 5（调用 `SendStreamingAsync(_conversation.Id, text, attachments, _sendCancellation.Token)`）与 Task 6 一致。
- `ApiProvider.MultimodalModelIds`（`IReadOnlySet<string>`）在 Task 3/4/6 一致。
- `ChatWindowViewModel` 构造第 10 参 `AppPaths? appPaths = null` 与测试 `CreateViewModel(..., appPaths: ...)` 一致。
