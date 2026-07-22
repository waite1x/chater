using Chater.Data;
using Chater.Models;
using Chater.Services;

namespace Chater.Tests;

public sealed class SkillServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SaveCustomAsync_CreatesAndVersionsCustomSkill()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var service = new SkillService(new SkillRepository(database));
        var now = DateTimeOffset.UtcNow;
        var draft = new Skill("custom", "Research", null, "Be rigorous.", null, false, true, 10, 0, now, now);

        var created = await service.SaveCustomAsync(draft);
        var updated = await service.SaveCustomAsync(created with { SystemPrompt = "Cite sources." });

        Assert.Equal(1, created.Version);
        Assert.Equal(2, updated.Version);
        Assert.Equal("Cite sources.", (await new SkillRepository(database).GetByIdAsync("custom"))?.SystemPrompt);
    }

    [Fact]
    public async Task SaveCustomAsync_RejectsBuiltInSkillChanges()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var service = new SkillService(new SkillRepository(database));
        var builtIn = await new SkillRepository(database).GetByIdAsync("builtin-chat");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveCustomAsync(builtIn! with { SystemPrompt = "Changed" }));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
