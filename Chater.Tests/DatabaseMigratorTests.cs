using Chater.Data;
using Microsoft.Data.Sqlite;

namespace Chater.Tests;

public sealed class DatabaseMigratorTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task MigrateAsync_CreatesSchemaAndSeedsBuiltInSkills_Idempotently()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        await using var connection = await database.OpenConnectionAsync();
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT COUNT(*) FROM SchemaMigrations;"));
        Assert.Equal(5L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Skills WHERE IsBuiltIn = 1;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Messages';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ProviderModels';"));
    }

    [Fact]
    public async Task OpenConnectionAsync_EnforcesForeignKeys()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);
        await migrator.MigrateAsync();
        await using var connection = await database.OpenConnectionAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Messages (Id, ConversationId, SequenceNo, Role, Content, Status, CreatedAt, UpdatedAt) VALUES ('message', 'missing', 1, 0, 'x', 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);";

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
