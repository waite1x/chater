using Chater.Data;
using Chater.Models;
using Chater.Models.Enums;
using Microsoft.Data.Sqlite;

namespace Chater.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SaveAsync_MakesOnlyLatestDefaultProviderDefault()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ApiProviderRepository(database);
        await repository.SaveAsync(CreateProvider("one", true));
        await repository.SaveAsync(CreateProvider("two", true));

        var providers = await repository.GetAllAsync();

        var defaultProvider = Assert.Single(providers, static provider => provider.IsDefault);
        Assert.Equal("two", defaultProvider.Id);
    }

    [Fact]
    public async Task SaveAsync_PersistsMultipleModelsForOneProvider()
    {
        var database = await CreateDatabaseAsync();
        var provider = CreateProvider("one", true) with { ModelIds = ["model-a", "model-b"], ModelId = "model-a" };

        await new ApiProviderRepository(database).SaveAsync(provider);

        var saved = await new ApiProviderRepository(database).GetByIdAsync(provider.Id);
        Assert.Equal(["model-a", "model-b"], saved?.ModelIds);
    }

    [Fact]
    public async Task GetEnabledAsync_ReturnsSeededSkillsInDisplayOrder()
    {
        var database = await CreateDatabaseAsync();

        var skills = await new SkillRepository(database).GetEnabledAsync();

        Assert.Equal(5, skills.Count);
        Assert.Equal("builtin-chat", skills[0].Id);
        Assert.All(skills, static skill => Assert.True(skill.IsBuiltIn));
    }

    [Fact]
    public async Task ConversationAndMessageQueries_ReturnPersistentHistoryInDisplayOrder()
    {
        var database = await CreateDatabaseAsync();
        var provider = CreateProvider("provider", true);
        await new ApiProviderRepository(database).SaveAsync(provider);
        var now = DateTimeOffset.UtcNow;
        var older = new Conversation("older", "Older", provider.Id, null, "{}", null, "agent", "hash", "1", "{}", SessionStatus.Active, false, now.AddMinutes(-1), now.AddMinutes(-1));
        var newer = new Conversation("newer", "Newer", provider.Id, null, "{}", null, "agent", "hash", "1", "{}", SessionStatus.Active, false, now, now);
        var conversations = new ConversationRepository(database);
        await conversations.SaveAsync(older);
        await conversations.SaveAsync(newer);
        var messages = new MessageRepository(database);
        await messages.AppendAsync(new Message("second", newer.Id, 2, MessageRole.Assistant, "second", MessageStatus.Completed, null, null, now, now));
        await messages.AppendAsync(new Message("first", newer.Id, 1, MessageRole.User, "first", MessageStatus.Completed, null, null, now, now));

        var recent = await conversations.GetRecentAsync();
        var history = await messages.GetByConversationAsync(newer.Id);

        Assert.Equal(["newer", "older"], recent.Select(conversation => conversation.Id));
        Assert.Equal(["first", "second"], history.Select(message => message.Content));
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private async Task<SqliteDatabase> CreateDatabaseAsync()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        return database;
    }

    private static ApiProvider CreateProvider(string id, bool isDefault) => new(
        id, id, ProviderType.OpenAi, "key", null, "model", isDefault, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
