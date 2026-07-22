using Chater.Models;

namespace Chater.Data;

public sealed class SkillRepository
{
    private readonly SqliteDatabase _database;

    public SkillRepository(SqliteDatabase database) => _database = database;

    public async Task<IReadOnlyList<Skill>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, SystemPrompt, Icon, IsBuiltIn, IsEnabled, SortOrder, Version, CreatedAt, UpdatedAt FROM Skills WHERE IsEnabled = 1 ORDER BY SortOrder, Name;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var skills = new List<Skill>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            skills.Add(new Skill(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5) == 1, reader.GetInt64(6) == 1, reader.GetInt32(7), reader.GetInt32(8), DateTimeOffset.Parse(reader.GetString(9)), DateTimeOffset.Parse(reader.GetString(10))));
        }

        return skills;
    }

    public async Task<Skill?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, SystemPrompt, Icon, IsBuiltIn, IsEnabled, SortOrder, Version, CreatedAt, UpdatedAt FROM Skills WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<Skill?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, SystemPrompt, Icon, IsBuiltIn, IsEnabled, SortOrder, Version, CreatedAt, UpdatedAt FROM Skills WHERE Name = $name COLLATE NOCASE;";
        command.Parameters.AddWithValue("$name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task SaveAsync(Skill skill, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Skills (Id, Name, Description, SystemPrompt, Icon, IsBuiltIn, IsEnabled, SortOrder, Version, CreatedAt, UpdatedAt) VALUES ($id, $name, $description, $systemPrompt, $icon, $isBuiltIn, $isEnabled, $sortOrder, $version, $createdAt, $updatedAt) ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name, Description = excluded.Description, SystemPrompt = excluded.SystemPrompt, Icon = excluded.Icon, IsEnabled = excluded.IsEnabled, SortOrder = excluded.SortOrder, Version = excluded.Version, UpdatedAt = excluded.UpdatedAt;";
        command.Parameters.AddWithValue("$id", skill.Id);
        command.Parameters.AddWithValue("$name", skill.Name);
        command.Parameters.AddWithValue("$description", skill.Description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$systemPrompt", skill.SystemPrompt);
        command.Parameters.AddWithValue("$icon", skill.Icon ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$isBuiltIn", skill.IsBuiltIn ? 1 : 0);
        command.Parameters.AddWithValue("$isEnabled", skill.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$sortOrder", skill.SortOrder);
        command.Parameters.AddWithValue("$version", skill.Version);
        command.Parameters.AddWithValue("$createdAt", skill.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", skill.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Skill Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5) == 1, reader.GetInt64(6) == 1, reader.GetInt32(7), reader.GetInt32(8), DateTimeOffset.Parse(reader.GetString(9)), DateTimeOffset.Parse(reader.GetString(10)));
}
