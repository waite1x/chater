using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Chater.Data;

public sealed class DatabaseMigrator
{
    private const int LatestVersion = 2;
    private readonly SqliteDatabase _database;

    public DatabaseMigrator(SqliteDatabase database) => _database = database;

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Version INTEGER NOT NULL PRIMARY KEY,
                AppliedAt TEXT NOT NULL
            );
            """, cancellationToken).ConfigureAwait(false);

        for (var version = 1; version <= LatestVersion; version++)
        {
            if (await IsAppliedAsync(connection, transaction, version, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await ExecuteAsync(connection, transaction, await ReadMigrationAsync(version, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "INSERT INTO SchemaMigrations (Version, AppliedAt) VALUES ($version, $appliedAt);", cancellationToken, ("$version", version), ("$appliedAt", DateTimeOffset.UtcNow.ToString("O"))).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsAppliedAsync(SqliteConnection connection, SqliteTransaction transaction, int version, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM SchemaMigrations WHERE Version = $version);";
        command.Parameters.AddWithValue("$version", version);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadMigrationAsync(int version, CancellationToken cancellationToken)
    {
        var resource = $"Chater.Data.Migrations.{version:0000}_{(version == 1 ? "InitialSchema" : "ProviderModels")}.sql";
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing database migration resource '{resource}'.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
