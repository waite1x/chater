using Chater.AI;
using Chater.AI.Conversations;
using Chater.AI.Providers;
using Chater.AI.Skills;
using Chater.AI.Tools;
using Chater.Data;
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

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private static MainWindowViewModel CreateViewModel(SqliteDatabase database, IWindowNavigationService? navigation = null) => new(
        new ProviderService(new ApiProviderRepository(database)),
        new SkillRepository(database),
        new ConversationService(new ConversationRepository(database)),
        new ChatService(new MessageRepository(database), new ConversationRepository(database), new ApiProviderRepository(database), new SessionRunLock(), new ChatToolRegistry([])),
        new ConversationRepository(database),
        new MessageRepository(database),
        navigation);

    private sealed class RecordingNavigation : IWindowNavigationService
    {
        public int SettingsCount { get; private set; }
        public int SkillSettingsCount { get; private set; }

        public void ShowSettings() => SettingsCount++;
        public void ShowSkillSettings() => SkillSettingsCount++;
        public void ShowChat() { }
    }
}
