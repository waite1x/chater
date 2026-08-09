# Chater 多模态支持设计

日期：2026-08-09
状态：已批准（用户确认）

## 概述

为 Chater 聊天智能体增加多模态（图片）支持：

1. 聊天窗口在提示词（技能）下拉框旁新增“添加图片”按钮，可一次选择多张图片，构建包含图片的多模态聊天内容。
2. API Key 设置页将模型列表拆分为“每行一个模型”，并为每个模型增加“是否多模态”选项；仅当所选模型为多模态时，聊天窗口才显示添加图片按钮。

已确认的决策：

- 模型列表 UI：每行一个模型 + 多模态勾选框。
- 图片历史持久化：发送时复制到应用数据目录 `attachments/`，历史重新打开时显示缩略图。
- 图片体积：原图字节直接以 base64 `DataContent` 发送，不压缩、不限制。

## 技术栈与约束

- Avalonia 11、CommunityToolkit.Mvvm（`[ObservableProperty]` 源生成）、MS DI、SQLite（`Microsoft.Data.Sqlite`）。
- Agent 运行时为 Microsoft.Agents.AI Harness；`AIAgent.RunStreamingAsync(ChatMessage, ...)` 重载已可用。
- AOT 兼容（`IsAotCompatible=true`）：新增 JSON 序列化必须使用 `ChaterJsonSerializerContext` 源生成上下文。
- 仓库中 `Converters\**` 目录已被 csproj 从编译/Avalonia 资源中排除，新增转换器不得放在该目录。

## 1. 数据模型与数据库

### 1.1 新增记录

`MessageAttachment(string FilePath, string FileName, string MimeType)`

- `FilePath`：应用目录内副本的绝对路径。
- `FileName`：原始文件名（用于展示）。
- `MimeType`：图片 MIME（如 `image/png`）。

### 1.2 ApiProvider

`ApiProvider` 增加 init 属性：

```csharp
public IReadOnlySet<string> MultimodalModelIds { get; init; } = [];
```

`ModelIds` 保持不变（向后兼容）。

### 1.3 迁移 0003

`Data/Migrations/0003_ProviderModelsMultimodal.sql`：

```sql
ALTER TABLE ProviderModels ADD COLUMN IsMultimodal INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Messages ADD COLUMN Attachments TEXT NULL;
```

`DatabaseMigrator.LatestVersion` 由 2 改为 3。

### 1.4 ApiProviderRepository

- `SaveAsync`：向 `ProviderModels` 插入时写入 `IsMultimodal`（按 `provider.MultimodalModelIds`）。
- `AddModelsAsync`：`SELECT ModelId, IsMultimodal ...`，构建 `MultimodalModelIds`。

### 1.5 Message

`Message` 记录增加 init 属性（不改构造签名）：

```csharp
public IReadOnlyList<MessageAttachment> Attachments { get; init; } = [];
```

### 1.6 MessageRepository

- `AppendAsync`：插入 `Attachments` 列（JSON 数组或 NULL）。附件 JSON 使用 `ChaterJsonSerializerContext` 序列化。
- `GetByConversationAsync`：读取 `Attachments` 列并反序列化（NULL → 空列表）。
- `UpdateContentAndStatusAsync`：不涉及附件，无需改动。

### 1.7 JSON 序列化上下文

`ChaterJsonSerializerContext` 增加：

```csharp
[JsonSerializable(typeof(MessageAttachment[]))]
```

### 1.8 AppPaths

- 新增 `AttachmentsDirectory => Path.Combine(ApplicationDataDirectory, "attachments")`。
- `EnsureCreated()` 中创建该目录。

## 2. 设置页：模型列表拆分 + 多模态标记

### 2.1 ProviderModelItem

新增 `ProviderModelItem : ViewModelBase`：

```csharp
[ObservableProperty] private string _modelId = string.Empty;
[ObservableProperty] private bool _isMultimodal;
```

### 2.2 ApiKeySettingsViewModel

- 用 `ObservableCollection<ProviderModelItem> ProviderModels` 取代多行文本 `ProviderModelId` 的编辑方式（`ProviderModelId` 字符串可移除，由行数据驱动）。
- `AddProviderCommand`：清空并新建一个空模型行。
- `OnSelectedProviderChanged`：由 `value.ModelIds` + `value.MultimodalModelIds` 回填行。
- `AddFetchedModel(string modelId)`：若行中不存在则追加一行（默认非多模态）。
- `AddModelCommand` / `RemoveModelCommand(ProviderModelItem)`：增删行。
- `BuildEditedProvider()`：从 `ProviderModels` 行生成 `ModelIds`（去重）与 `MultimodalModelIds`；`activeModel` 取第一行。

### 2.3 ApiKeySettingsView.axaml

- 移除多行 TextBox，替换为 `ItemsControl` 模型行列表：每行 = `TextBox`（ModelId）+ `CheckBox`（IsMultimodal，文案 `Localization[Multimodal]`）+ 删除按钮。
- 下方“添加模型”按钮（`AddModelCommand`）。
- 保留“从 API 抓取模型”区域，点击后追加到行列表。

## 3. 聊天窗口：添加图片按钮 + 附件区

### 3.1 AttachmentViewModel

```csharp
[ObservableProperty] private string _filePath;
// FileName、MimeType 只读
```

### 3.2 ChatWindowViewModel

- `ObservableCollection<AttachmentViewModel> Attachments`。
- `ShowAddAttachmentButton`：`SelectedProvider` 非空 且 `SelectedProvider.MultimodalModelIds.Contains(SelectedModelId)`。
- `AddFilesCommand`：
  - 使用 `TopLevel.GetTopLevel(...).StorageProvider.OpenFilePickerAsync`，过滤图片扩展名 `png/jpg/jpeg/gif/webp/bmp`，`AllowMultiple=true`。
  - 每个选中文件复制到 `AppPaths.AttachmentsDirectory`（文件名带时间戳/Guid 前缀避免冲突），创建 `AttachmentViewModel`。
- `RemoveAttachmentCommand(AttachmentViewModel)`：移除并删除尚未持久化的副本文件。
- `OnSelectedProviderChanged` / `OnSelectedModelIdChanged`：若新选择非多模态，清空 `Attachments`（并删除未发送副本）。
- `CanSend`：`!IsSending && (!string.IsNullOrWhiteSpace(Draft) || Attachments.Count > 0)`。
- `SendAsync`：构建附件列表传入 ChatService；发送后清空 `Attachments`（副本文件已持久化，不删除）。
- 发送期间在用户消息气泡中同时展示附件。

### 3.3 ChatWindow.axaml

- 在提示词（技能）`ComboBox` 所在行（底部 DockPanel）其右侧新增“添加图片”按钮：
  - `Command="{Binding AddFilesCommand}"`
  - `IsVisible="{Binding ShowAddAttachmentButton}"`
  - 图标：MaterialIcon `Image` / `ImageMultiple`，ToolTip `Localization[AddImage]`。
- 输入 `TextBox` 上方新增附件预览条：横向 `WrapPanel`，每项为 `Image`（缩略图，`Stretch=Uniform`，固定高度）+ 移除按钮。

## 4. 多模态发送链路

### 4.1 ChatService.SendStreamingAsync

签名改为（附件元数据由 ViewModel 传入，ChatService 负责读取字节、构造 ChatMessage 与持久化，保持 base64/DataContent 关注点在 AI 层）：

```csharp
public IAsyncEnumerable<string> SendStreamingAsync(
    string conversationId,
    string text,
    IReadOnlyList<MessageAttachment> attachments,
    CancellationToken cancellationToken = default)
```

- `attachments` 携带已复制副本的 `FilePath`、`FileName`、`MimeType`（由 `ChatWindowViewModel` 在发送时构造 `MessageAttachment` 列表）。
- 构造 `var message = new ChatMessage(ChatRole.User, contents)`：
  - `new TextContent(text)`
  - 每个附件 `new DataContent(File.ReadAllBytes(a.FilePath), a.MimeType)`（原图，OpenAI/扩展 AI 客户端编码为 data URI）。
- 标题：`ConversationService.CreateTitle(text)`，若 `text` 为空则回退占位文案（如 `[Image]`）。
- 用户消息持久化：`Content = text`，`Attachments = attachments`。
- 调用 `agent.RunStreamingAsync(message, session, cancellationToken: ...)`（替换原字符串重载）。

### 4.2 图片数据

- 构造 `ChatMessage(ChatRole.User, contents)`：
  - `new TextContent(text)`
  - 每个附件 `new DataContent(File.ReadAllBytes(path), mimeType)`（原图 base64，由 OpenAI/扩展AI 客户端编码为 data URI）。

## 5. 历史渲染

- `ChatMessageViewModel` 增加附件路径集合（或直接持有 `IReadOnlyList<MessageAttachment>`）。
- 构造时由 `Message.Attachments` 传入；发送时由 `AttachmentViewModel` 传入。
- `ConversationMessagesView.axaml`：用户气泡内、Markdown 上方，绑定附件缩略图 `ItemsControl`；`Image.Source` 通过新转换器（路径 → `Bitmap`，位于非 `Converters\**` 目录）绑定。
- 复制/上下文菜单逻辑不受影响（仍针对 Markdown 文本）。

## 6. 本地化

在三个 resx（`Resources.resx` / `Resources.zh-CN.resx` / `Resources.zh-TW.resx`）中新增键：

- `AddImage`：Add image / 添加图片 / 新增圖片
- `Multimodal`：Multimodal / 多模态 / 多模態
- `RemoveAttachment`：Remove / 移除 / 移除
- `AddModel`：Add model / 添加模型 / 新增模型
- `AttachmentHint`（可选）：附件的辅助提示文案

## 7. 测试

- `RepositoryTests`：新增 ProviderModels 多模态标记往返（Save → GetAll 含 `MultimodalModelIds`）；新增 Message 附件持久化往返（Append → GetByConversationAsync 含附件）。
- `ChatServiceTests`：验证传入含 `DataContent` 的 `ChatMessage` 时，用户消息以正确文本+附件持久化；空文本标题回退。
- `ChatWindowViewModelTests`：附件添加/移除；`ShowAddAttachmentButton` 随所选模型多模态与否切换；非多模态模型下切换模型清空附件；`CanSend` 在仅附件无文本时为真。
- `DatabaseMigratorTests`：迁移到版本 3。
- 回归：现有 42 项测试保持通过。

## 影响文件清单

- 新增：`Data/Migrations/0003_ProviderModelsMultimodal.sql`
- 新增：`AI/Conversations/MessageAttachment.cs`
- 新增：`ViewModels/ProviderModelItem.cs`
- 新增：`ViewModels/AttachmentViewModel.cs`
- 新增：缩略图转换器（路径 → Bitmap）
- 修改：`AI/Providers/ApiProvider.cs`、`AI/Providers/ApiProviderRepository.cs`
- 修改：`AI/Conversations/Message.cs`、`AI/Tools/MessageRepository.cs`（命名空间 `Chater.Data`）
- 修改：`AI/ChatService.cs`
- 修改：`AI/ChaterJsonSerializerContext.cs`
- 修改：`Data/DatabaseMigrator.cs`
- 修改：`Services/AppPaths.cs`
- 修改：`ViewModels/ApiKeySettingsViewModel.cs`、`Views/Settings/ApiKeySettingsView.axaml`
- 修改：`ViewModels/ChatWindowViewModel.cs`、`Views/ChatWindow.axaml`、`ViewModels/ChatMessageViewModel.cs`、`Views/ConversationMessagesView.axaml`
- 修改：`Localization/Resources.resx`、`Resources.zh-CN.resx`、`Resources.zh-TW.resx`
- 修改：测试文件（见第 7 节）
