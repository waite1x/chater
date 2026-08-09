using Chater.AI;
using Chater.AI.Conversations;
using Chater.AI.Providers;
using Chater.AI.Tools;
using Chater.Data;
using Microsoft.Extensions.AI;

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
        var service = new ChatService(new MessageRepository(database), new ConversationRepository(database), new ApiProviderRepository(database), new SessionRunLock(), new ChatToolRegistry([]));

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in service.SendStreamingAsync("conversation", "hello"))
            {
            }
        });
    }

    [Fact]
    public void BuildUserMessage_IncludesTextAndDataContent()
    {
        var dirPath = Path.Combine(Path.GetTempPath(), "Chater.Tests");
        var imagePath = Path.Combine(dirPath, $"{Guid.NewGuid():N}.png");
        try
        {
            Directory.CreateDirectory(dirPath);
            File.WriteAllBytes(imagePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var attachments = new[] { new MessageAttachment(imagePath, "a.png", "image/png") };

            var message = ChatService.BuildUserMessage("look", attachments);

            Assert.Equal(ChatRole.User, message.Role);
            Assert.Equal("look", message.Text);
            var data = Assert.Single(message.Contents.OfType<DataContent>());
            Assert.Equal("image/png", data.MediaType);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], data.Data.ToArray());
        }
        finally
        {
            if (File.Exists(imagePath)) File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task SendStreamingAsync_PersistsUserMessageWithAttachments_BeforeProviderRejection()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        await SeedConversationAsync(database);
        var service = new ChatService(new MessageRepository(database), new ConversationRepository(database), new ApiProviderRepository(database), new SessionRunLock(), new ChatToolRegistry([]));
        var dirPath = Path.Combine(Path.GetTempPath(), "Chater.Tests");
        var imagePath = Path.Combine(dirPath, $"{Guid.NewGuid():N}.png");
        try
        {
            Directory.CreateDirectory(dirPath);
            File.WriteAllBytes(imagePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var attachments = new[] { new MessageAttachment(imagePath, "a.png", "image/png") };

            await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                await foreach (var _ in service.SendStreamingAsync("conversation", "look", attachments))
                {
                }
            });

            var messages = await new MessageRepository(database).GetByConversationAsync("conversation");
            var user = Assert.Single(messages, static m => m.Role == MessageRole.User);
            Assert.Equal("look", user.Content);
            var attachment = Assert.Single(user.Attachments);
            Assert.Equal("image/png", attachment.MimeType);
        }
        finally
        {
            if (File.Exists(imagePath)) File.Delete(imagePath);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        var dirPath = Path.Combine(Path.GetTempPath(), "Chater.Tests");
        try { if (Directory.Exists(dirPath)) Directory.Delete(dirPath); } catch (IOException) { /* directory not empty yet */ }
    }

    private static async Task SeedConversationAsync(SqliteDatabase database)
    {
        await using var c = await database.OpenConnectionAsync(); await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO ApiProviders (Id,Name,ProviderType,ApiKey,ModelId,CreatedAt,UpdatedAt) VALUES ('p','p',1,'k','m',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP); INSERT INTO Conversations (Id,Title,ProviderId,ProviderConfiguration,AgentType,AgentConfigurationHash,MafVersion,SessionState,SessionStatus,CreatedAt,UpdatedAt) VALUES ('conversation','c','p','{\"ProviderType\":1,\"ModelId\":\"m\",\"Endpoint\":null,\"SystemPrompt\":null}','a','h','1','{}',0,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);";
        await cmd.ExecuteNonQueryAsync();
    }

}
