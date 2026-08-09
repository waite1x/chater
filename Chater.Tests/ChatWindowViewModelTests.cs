using Chater.AI;
using Chater.AI.Conversations;
using Chater.AI.Providers;
using Chater.AI.Skills;
using Chater.AI.Tools;
using Chater.Data;
using Chater.Services;
using Chater.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Chater.Tests;

public sealed class ChatWindowViewModelTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task PrepareNewSession_SelectsDefaultProviderAndFirstSkill()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        viewModel.AppState.Providers.Add(CreateProvider());
        viewModel.AppState.Skills.Add(CreateSkill());

        viewModel.PrepareNewSession();

        Assert.Equal("provider", viewModel.SelectedProvider?.Id);
        Assert.Equal("builtin-chat", viewModel.SelectedSkill?.Id);
    }

    [Fact]
    public async Task SelectedProvider_ExposesAllModelsAndSelectsTheActiveModel()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        viewModel.AppState.Providers.Add(CreateProvider(modelIds: ["model-a", "model-b"]));

        viewModel.PrepareNewSession();

        Assert.Equal(["model-a", "model-b"], viewModel.AvailableModels);
        Assert.Equal("model-a", viewModel.SelectedModelId);
    }

    [Fact]
    public async Task SelectingModel_UpdatesSelectionAndDisplayNames()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        viewModel.AppState.Providers.Add(CreateProvider(modelIds: ["model-a", "model-b"]));
        viewModel.PrepareNewSession();

        viewModel.SelectedModelId = "model-b";

        Assert.Equal("provider", viewModel.SelectedProvider?.Id);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.Equal("Default", viewModel.SelectedProviderDisplayName);
        Assert.Equal("model-b", viewModel.SelectedModelDisplayName);
    }

    [Fact]
    public void NavigationCommands_OpenCorrespondingWorkspaceWindows()
    {
        var database = new SqliteDatabase(_path);
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

    private static ChatWindowViewModel CreateViewModel(SqliteDatabase database, IWindowNavigationService? navigation = null)
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
            navigation);
    }

    private static ApiProvider CreateProvider(string[]? modelIds = null) => new(
        "provider", "Default", ProviderType.OpenAi, "key", null, "model-a", true, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
    {
        ModelIds = modelIds ?? ["model-a"]
    };

    private static Skill CreateSkill() => new(
        "builtin-chat", "通用对话", null, "你是 Chater，一个有用的 AI 助手。", "💬", true, true, 0, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class RecordingNavigation : IWindowNavigationService
    {
        public int SettingsCount { get; private set; }
        public int SkillSettingsCount { get; private set; }

        public void ShowSettings() => SettingsCount++;
        public void ShowSkillSettings() => SkillSettingsCount++;
    }
}
