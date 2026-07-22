using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chater.Data;
using Chater.Models;
using Chater.Models.Enums;

namespace Chater.Services;

public sealed class ConversationService(ConversationRepository conversations)
{
    private const int ConversationTitleMaxLength = 30;

    public static string CreateTitle(string message)
    {
        var title = message.Trim();
        return title.Length <= ConversationTitleMaxLength
            ? title
            : string.Concat(title.AsSpan(0, ConversationTitleMaxLength), "...");
    }

    public async Task<Conversation> CreateAsync(ApiProvider provider, Skill? skill, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var now = DateTimeOffset.UtcNow;
        var configuration = JsonSerializer.Serialize(new ProviderSnapshot(provider.ProviderType, provider.ModelId, provider.Endpoint, skill?.SystemPrompt), ChaterJsonSerializerContext.Default.ProviderSnapshot);
        var conversation = new Conversation(
            Guid.NewGuid().ToString("N"),
            skill?.Name ?? "新对话",
            provider.Id,
            skill?.Id,
            configuration,
            skill?.Version,
            "maf-chat-client-agent",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration))),
            typeof(ConversationService).Assembly.GetName().Version?.ToString() ?? "unknown",
            "{}",
            SessionStatus.Active,
            false,
            now,
            now);
        await conversations.SaveAsync(conversation, cancellationToken).ConfigureAwait(false);
        return conversation;
    }

    public static SessionSnapshot GetPersistedSessionSnapshot(Conversation conversation)
    {
        var configuration = JsonSerializer.Deserialize(conversation.ProviderConfiguration, ChaterJsonSerializerContext.Default.ProviderSnapshot) ?? throw new InvalidOperationException("Conversation provider snapshot is invalid.");
        return new SessionSnapshot(conversation.AgentType, conversation.ProviderId, configuration.ModelId, conversation.SkillVersion, conversation.MafVersion, conversation.AgentConfigurationHash, conversation.SessionState);
    }

    public static SessionSnapshot GetRequestedSessionSnapshot(Conversation conversation, ApiProvider provider)
    {
        var configuration = JsonSerializer.Deserialize(conversation.ProviderConfiguration, ChaterJsonSerializerContext.Default.ProviderSnapshot) ?? throw new InvalidOperationException("Conversation provider snapshot is invalid.");
        var model = provider.ModelIds.Contains(configuration.ModelId, StringComparer.OrdinalIgnoreCase) ? configuration.ModelId : provider.ModelId;
        var currentConfiguration = JsonSerializer.Serialize(new ProviderSnapshot(provider.ProviderType, model, provider.Endpoint, configuration.SystemPrompt), ChaterJsonSerializerContext.Default.ProviderSnapshot);
        return new SessionSnapshot(conversation.AgentType, provider.Id, model, conversation.SkillVersion, conversation.MafVersion, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(currentConfiguration))), conversation.SessionState);
    }
}

internal sealed record ProviderSnapshot(ProviderType ProviderType, string ModelId, string? Endpoint, string? SystemPrompt);
