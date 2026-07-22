using Chater.Data;
using Chater.Models.Enums;
using Chater.Services;

namespace Chater.Tests;

public sealed class ChatServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SendStreamingAsync_UsesMafAgentAndRejectsUnsupportedProvider()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        await SeedConversationAsync(database);
        var service = new ChatService(new MessageRepository(database), new ConversationRepository(database), new ApiProviderRepository(database), new SessionRunLock());

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in service.SendStreamingAsync("conversation", "hello"))
            {
            }
        });
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    private static async Task SeedConversationAsync(SqliteDatabase database)
    {
        await using var c = await database.OpenConnectionAsync(); await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO ApiProviders (Id,Name,ProviderType,ApiKey,ModelId,CreatedAt,UpdatedAt) VALUES ('p','p',1,'k','m',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP); INSERT INTO Conversations (Id,Title,ProviderId,ProviderConfiguration,AgentType,AgentConfigurationHash,MafVersion,SessionState,SessionStatus,CreatedAt,UpdatedAt) VALUES ('conversation','c','p','{\"ProviderType\":1,\"ModelId\":\"m\",\"Endpoint\":null,\"SystemPrompt\":null}','a','h','1','{}',0,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);";
        await cmd.ExecuteNonQueryAsync();
    }

}
