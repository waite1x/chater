using Chater.Models;
using Chater.Models.Enums;

namespace Chater.Data;

public sealed class ConversationRepository
{
    private readonly SqliteDatabase _database;

    public ConversationRepository(SqliteDatabase database) => _database = database;

    public async Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Conversations (Id, Title, ProviderId, SkillId, ProviderConfiguration, SkillVersion, AgentType, AgentConfigurationHash, MafVersion, SessionState, SessionStatus, IsArchived, CreatedAt, UpdatedAt)
            VALUES ($id, $title, $providerId, $skillId, $providerConfiguration, $skillVersion, $agentType, $hash, $mafVersion, $sessionState, $sessionStatus, $isArchived, $createdAt, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET Title = excluded.Title, SessionState = excluded.SessionState, SessionStatus = excluded.SessionStatus, IsArchived = excluded.IsArchived, UpdatedAt = excluded.UpdatedAt;
            """;
        Add(command, "$id", conversation.Id); Add(command, "$title", conversation.Title); Add(command, "$providerId", conversation.ProviderId); Add(command, "$skillId", conversation.SkillId ?? (object)DBNull.Value); Add(command, "$providerConfiguration", conversation.ProviderConfiguration); Add(command, "$skillVersion", conversation.SkillVersion ?? (object)DBNull.Value); Add(command, "$agentType", conversation.AgentType); Add(command, "$hash", conversation.AgentConfigurationHash); Add(command, "$mafVersion", conversation.MafVersion); Add(command, "$sessionState", conversation.SessionState); Add(command, "$sessionStatus", (int)conversation.SessionStatus); Add(command, "$isArchived", conversation.IsArchived ? 1 : 0); Add(command, "$createdAt", conversation.CreatedAt.ToString("O")); Add(command, "$updatedAt", conversation.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Conversation?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, ProviderId, SkillId, ProviderConfiguration, SkillVersion, AgentType, AgentConfigurationHash, MafVersion, SessionState, SessionStatus, IsArchived, CreatedAt, UpdatedAt FROM Conversations WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new Conversation(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), (SessionStatus)reader.GetInt32(10), reader.GetInt64(11) == 1, DateTimeOffset.Parse(reader.GetString(12)), DateTimeOffset.Parse(reader.GetString(13)));
    }

    public async Task<IReadOnlyList<Conversation>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, ProviderId, SkillId, ProviderConfiguration, SkillVersion, AgentType, AgentConfigurationHash, MafVersion, SessionState, SessionStatus, IsArchived, CreatedAt, UpdatedAt FROM Conversations WHERE IsArchived = 0 ORDER BY UpdatedAt DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var conversations = new List<Conversation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            conversations.Add(new Conversation(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), (SessionStatus)reader.GetInt32(10), reader.GetInt64(11) == 1, DateTimeOffset.Parse(reader.GetString(12)), DateTimeOffset.Parse(reader.GetString(13))));
        }

        return conversations;
    }

    private static void Add(Microsoft.Data.Sqlite.SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
}
