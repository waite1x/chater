using Avalonia.Media;
using Chater.Data;
using Chater.Models;
using Chater.Models.Enums;
using Chater.Providers;
using Chater.Services;
using Chater.ViewModels;

namespace Chater.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task LoadAsync_SelectsDefaultEnabledProviderAndBuiltInSkill()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        await new ApiProviderRepository(database).SaveAsync(new ApiProvider("provider", "Default", ProviderType.OpenAi, "key", null, "model", true, true, now, now));
        var viewModel = CreateViewModel(database);

        await viewModel.LoadAsync();

        Assert.Equal("provider", viewModel.SelectedProvider?.Id);
        Assert.NotNull(viewModel.SelectedSkill);
        Assert.Equal("已就绪。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveProviderCommand_LeavesExistingApiKeyUntouchedWhenInputIsBlank()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        await new ApiProviderRepository(database).SaveAsync(new ApiProvider("provider", "Default", ProviderType.OpenAi, "secret", null, "model", true, true, now, now));
        var viewModel = CreateViewModel(database);
        await viewModel.LoadAsync();

        viewModel.ProviderName = "Renamed";
        viewModel.ProviderApiKey = string.Empty;
        viewModel.SaveProviderCommand.Execute(null);
        await viewModel.SaveProviderCommand.ExecutionTask!;

        var provider = await new ApiProviderRepository(database).GetByIdAsync("provider");
        Assert.Equal("Renamed", provider?.Name);
        Assert.Equal("secret", provider?.ApiKey);
    }

    [Fact]
    public async Task LoadAsync_ExposesAllModelsAndSelectsTheActiveModel()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        await new ApiProviderRepository(database).SaveAsync(new ApiProvider("provider", "Default", ProviderType.OpenAi, "secret", null, "model-a", true, true, now, now)
        {
            ModelIds = ["model-a", "model-b"]
        });
        var viewModel = CreateViewModel(database);

        await viewModel.LoadAsync();

        Assert.Equal(["model-a", "model-b"], viewModel.AvailableModels);
        Assert.Equal("model-a", viewModel.SelectedModelId);
    }

    [Fact]
    public async Task ProviderModelMenu_SelectsProviderAndModelTogether()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        await new ApiProviderRepository(database).SaveAsync(new ApiProvider("provider", "Default", ProviderType.OpenAi, "secret", null, "model-a", true, true, now, now)
        {
            ModelIds = ["model-a", "model-b"]
        });
        var viewModel = CreateViewModel(database);
        await viewModel.LoadAsync();

        var model = Assert.Single(viewModel.ProviderModelMenuItems).Models.Single(item => item.ModelId == "model-b");
        model.SelectCommand.Execute(null);

        Assert.Equal("provider", viewModel.SelectedProvider?.Id);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.Equal("Default", viewModel.SelectedProviderDisplayName);
        Assert.Equal("model-b", viewModel.SelectedModelDisplayName);
    }

    [Fact]
    public async Task SaveSkillCommand_AddsCustomSkillToSelectionList()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        await viewModel.LoadAsync();

        viewModel.AddSkillCommand.Execute(null);
        viewModel.SkillName = "Research";
        viewModel.SkillPrompt = "Cite primary sources.";
        viewModel.SaveSkillCommand.Execute(null);
        await viewModel.SaveSkillCommand.ExecutionTask!;

        Assert.Contains(viewModel.Skills, skill => skill.Name == "Research" && skill.Version == 1);
    }

    [Fact]
    public async Task DeleteProviderCommand_DisablesProviderAndRemovesItFromList()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var repository = new ApiProviderRepository(database);
        await repository.SaveAsync(new ApiProvider("provider", "Default", ProviderType.OpenAi, "key", null, "model", true, true, now, now));
        var viewModel = CreateViewModel(database);
        await viewModel.LoadAsync();

        viewModel.DeleteProviderCommand.Execute(viewModel.SelectedProvider);
        await viewModel.DeleteProviderCommand.ExecutionTask!;

        Assert.Empty(viewModel.Providers);
        Assert.False((await repository.GetByIdAsync("provider"))?.IsEnabled);
    }

    [Fact]
    public async Task DeleteProviderCommand_DoesNotDeleteWhenConfirmationIsCancelled()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var repository = new ApiProviderRepository(database);
        await repository.SaveAsync(new ApiProvider("provider", "Default", ProviderType.OpenAi, "key", null, "model", true, true, now, now));
        var confirmation = new RecordingConfirmation(false);
        var viewModel = CreateViewModel(database, confirmation: confirmation);
        await viewModel.LoadAsync();

        viewModel.DeleteProviderCommand.Execute(viewModel.SelectedProvider);
        await viewModel.DeleteProviderCommand.ExecutionTask!;

        Assert.Equal(1, confirmation.Count);
        Assert.True((await repository.GetByIdAsync("provider"))?.IsEnabled ?? false);
    }

    [Fact]
    public async Task DeleteSkillCommand_DisablesCustomSkillAndRemovesItFromList()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        await viewModel.LoadAsync();

        viewModel.AddSkillCommand.Execute(null);
        viewModel.SkillName = "Research";
        viewModel.SkillPrompt = "Cite primary sources.";
        viewModel.SaveSkillCommand.Execute(null);
        await viewModel.SaveSkillCommand.ExecutionTask!;
        var skill = Assert.Single(viewModel.Skills, item => item.Name == "Research");

        viewModel.DeleteSkillCommand.Execute(skill);
        await viewModel.DeleteSkillCommand.ExecutionTask!;

        Assert.DoesNotContain(viewModel.Skills, item => item.Name == "Research");
    }

    [Fact]
    public async Task ClearShortcutCommand_ClearsPersistedShortcut()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);

        viewModel.ChatShortcut = "Ctrl+Alt+C";
        viewModel.ClearShortcutCommand.Execute(null);
        await viewModel.ClearShortcutCommand.ExecutionTask!;

        Assert.Empty(viewModel.ChatShortcut);
        Assert.Equal(string.Empty, await new AppSettingRepository(database).GetAsync(AppSettingsService.ChatShortcutKey));
    }

    [Fact]
    public async Task ClearNewChatWindowShortcutCommand_ClearsPersistedShortcut()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);

        viewModel.NewChatWindowShortcut = "Ctrl+Alt+N";
        viewModel.ClearNewChatWindowShortcutCommand.Execute(null);
        await viewModel.ClearNewChatWindowShortcutCommand.ExecutionTask!;

        Assert.Empty(viewModel.NewChatWindowShortcut);
        Assert.Equal(string.Empty, await new AppSettingRepository(database).GetAsync(AppSettingsService.NewChatWindowShortcutKey));
    }

    [Fact]
    public async Task NavigationCommands_OpenCorrespondingWorkspaceWindows()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var navigation = new RecordingNavigation();
        var viewModel = CreateViewModel(database, navigation);

        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.OpenSkillWorkbenchCommand.Execute(null);

        Assert.Equal(1, navigation.SettingsCount);
        Assert.Equal(1, navigation.SkillSettingsCount);
    }

    [Fact]
    public void SettingsTabCommands_SelectApiKeyAndSkillPages()
    {
        var database = new SqliteDatabase(_path);
        var viewModel = CreateViewModel(database);

        viewModel.ShowSkillSettingsCommand.Execute(null);
        Assert.Equal(1, viewModel.SettingsTabIndex);
        viewModel.ShowApiKeySettingsCommand.Execute(null);
        Assert.Equal(0, viewModel.SettingsTabIndex);
    }

    [Fact]
    public async Task LoadAsync_LoadsPersistedThemeAndShortcut()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var settings = new AppSettingRepository(database);
        await settings.SaveAsync(AppSettingsService.ThemeKey, "dark");
        await settings.SaveAsync(AppSettingsService.AccentColorKey, "#F43F5E");
        await settings.SaveAsync(AppSettingsService.ChatShortcutKey, "Ctrl+Alt+C");
        await settings.SaveAsync(AppSettingsService.NewChatWindowShortcutKey, "Ctrl+Alt+N");

        var viewModel = CreateViewModel(database);
        await viewModel.LoadAsync();

        Assert.Equal("dark", viewModel.SelectedTheme?.Key);
        Assert.Equal(Color.Parse("#F43F5E"), viewModel.AccentColor);
        Assert.Equal("Ctrl+Alt+C", viewModel.ChatShortcut);
        Assert.Equal("Ctrl+Alt+N", viewModel.NewChatWindowShortcut);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static MainWindowViewModel CreateViewModel(SqliteDatabase database, IWindowNavigationService? navigation = null, IConfirmationService? confirmation = null) => new(
        new ProviderService(new ApiProviderRepository(database)),
        new SkillRepository(database),
        new ConversationService(new ConversationRepository(database)),
        new ChatService(new MessageRepository(database), new ConversationRepository(database), new ApiProviderRepository(database), new SessionRunLock()),
        new ConversationRepository(database),
        new MessageRepository(database),
        new SkillService(new SkillRepository(database)),
        new AppSettingsService(new AppSettingRepository(database)),
        navigation,
        confirmation: confirmation);

    private sealed class RecordingNavigation : IWindowNavigationService
    {
        public int SettingsCount { get; private set; }
        public int SkillSettingsCount { get; private set; }

        public void ShowSettings() => SettingsCount++;
        public void ShowSkillSettings() => SkillSettingsCount++;
        public void ShowChat() { }
    }

    private sealed class RecordingConfirmation(bool result) : IConfirmationService
    {
        public int Count { get; private set; }

        public Task<bool> ConfirmDeleteAsync(string itemName)
        {
            Count++;
            return Task.FromResult(result);
        }
    }
}
