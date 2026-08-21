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
        Assert.Equal(6L, await ScalarAsync(connection, "SELECT COUNT(*) FROM SchemaMigrations;"));
        Assert.Equal(5L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Skills WHERE IsBuiltIn = 1;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Messages';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ProviderModels';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ProviderModels') WHERE name = 'IsMultimodal';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('Messages') WHERE name = 'Attachments';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('Messages') WHERE name = 'Reasoning';"));
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

    [Fact]
    public async Task MigrateAsync_UpgradesAnExistingV3DatabaseWithMessageReasoning()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);
        await migrator.MigrateAsync();

        // Simulate a database created by the previous application version.
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "ALTER TABLE Messages DROP COLUMN Reasoning; DELETE FROM SchemaMigrations WHERE Version = 4;";
            await command.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();

        await using var upgraded = await database.OpenConnectionAsync();
        Assert.Equal(1L, await ScalarAsync(upgraded, "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 4;"));
        Assert.Equal(1L, await ScalarAsync(upgraded, "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 5;"));
        Assert.Equal(1L, await ScalarAsync(upgraded, "SELECT COUNT(*) FROM pragma_table_info('Messages') WHERE name = 'Reasoning';"));
    }

    [Fact]
    public async Task MigrateAsync_RepairsAnExistingV4DatabaseMissingMessageReasoning()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);
        await migrator.MigrateAsync();

        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "ALTER TABLE Messages DROP COLUMN Reasoning; DELETE FROM SchemaMigrations WHERE Version = 5;";
            await command.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();

        await using var repaired = await database.OpenConnectionAsync();
        Assert.Equal(1L, await ScalarAsync(repaired, "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 5;"));
        Assert.Equal(1L, await ScalarAsync(repaired, "SELECT COUNT(*) FROM pragma_table_info('Messages') WHERE name = 'Reasoning';"));
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
