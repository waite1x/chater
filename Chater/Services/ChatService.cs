using System.ClientModel;
using System.Text.Json;
using Chater.Data;
using Chater.Models;
using Chater.Models.Enums;
using Microsoft.Agents.AI;
using OpenAI;
using OpenAI.Chat;

namespace Chater.Services;

/// <summary>
/// Runs each conversation as its own MAF AIAgent and persists that agent's session.
/// </summary>
public sealed class ChatService(
    MessageRepository messages,
    ConversationRepository conversations,
    ApiProviderRepository providers,
    SessionRunLock sessionLock)
{
    public async IAsyncEnumerable<string> SendStreamingAsync(string conversationId, string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var lease = await sessionLock.AcquireAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException($"Conversation '{conversationId}' does not exist.");
        var provider = await providers.GetByIdAsync(conversation.ProviderId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException($"Provider '{conversation.ProviderId}' does not exist.");
        if (!provider.IsEnabled)
        {
            throw new InvalidOperationException($"Provider '{provider.Name}' is disabled.");
        }

        var snapshot = ReadProviderSnapshot(conversation.ProviderConfiguration);
        provider = provider with { ModelId = snapshot.ModelId, Endpoint = snapshot.Endpoint };
        if (conversation.SessionState != "{}" && !SessionStateValidator.CanRestore(ConversationService.GetPersistedSessionSnapshot(conversation), ConversationService.GetRequestedSessionSnapshot(conversation, provider)))
        {
            await conversations.SaveAsync(conversation with { SessionStatus = SessionStatus.Invalid, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The saved session no longer matches its provider configuration. Create a new conversation to continue.");
        }

        var now = DateTimeOffset.UtcNow;
        var userSequenceNo = await messages.GetNextSequenceNoAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (userSequenceNo == 1)
        {
            conversation = conversation with { Title = ConversationService.CreateTitle(message), UpdatedAt = now };
            await conversations.SaveAsync(conversation, cancellationToken).ConfigureAwait(false);
        }

        await messages.AppendAsync(new Message(Guid.NewGuid().ToString("N"), conversationId, userSequenceNo, MessageRole.User, message, MessageStatus.Completed, null, null, now, now), cancellationToken).ConfigureAwait(false);

        var assistantMessageId = Guid.NewGuid().ToString("N");
        await messages.AppendAsync(new Message(assistantMessageId, conversationId, userSequenceNo + 1, MessageRole.Assistant, string.Empty, MessageStatus.Streaming, null, null, now, now), cancellationToken).ConfigureAwait(false);

        var agent = CreateAgent(provider, snapshot.SystemPrompt);
        var session = await RestoreOrCreateSessionAsync(agent, conversation, cancellationToken).ConfigureAwait(false);
        var content = string.Empty;
        try
        {
            await using var updates = agent.RunStreamingAsync(message, session, cancellationToken: cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await updates.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await messages.UpdateContentAndStatusAsync(assistantMessageId, content, MessageStatus.Cancelled, "cancelled", "The response was cancelled.", CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception exception)
                {
                    await messages.UpdateContentAndStatusAsync(assistantMessageId, content, MessageStatus.Failed, "provider_error", exception.Message, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }

                if (!hasNext)
                {
                    break;
                }

                var update = updates.Current;
                if (string.IsNullOrEmpty(update.Text))
                {
                    continue;
                }

                content += update.Text;
                await messages.UpdateContentAndStatusAsync(assistantMessageId, content, MessageStatus.Streaming, cancellationToken: cancellationToken).ConfigureAwait(false);
                yield return update.Text;
            }

            await messages.UpdateContentAndStatusAsync(assistantMessageId, content, MessageStatus.Completed, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            var serializedSession = await agent.SerializeSessionAsync(session, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            await conversations.SaveAsync(conversation with
            {
                SessionState = serializedSession.GetRawText(),
                SessionStatus = SessionStatus.Restorable,
                UpdatedAt = DateTimeOffset.UtcNow
            }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static ProviderSnapshot ReadProviderSnapshot(string configuration)
    {
        return JsonSerializer.Deserialize(configuration, ChaterJsonSerializerContext.Default.ProviderSnapshot)
            ?? throw new InvalidOperationException("Conversation provider snapshot is invalid.");
    }

    private static AIAgent CreateAgent(ApiProvider provider, string? instructions)
    {
        if (provider.ProviderType is ProviderType.Anthropic)
        {
            throw new NotSupportedException("Anthropic will use its dedicated provider adapter.");
        }

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(provider.Endpoint))
        {
            options.Endpoint = new Uri(provider.Endpoint, UriKind.Absolute);
        }

        var apiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? "ollama" : provider.ApiKey;
        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options).GetChatClient(provider.ModelId);
        return client.AsAIAgent(instructions: instructions);
    }

    private static async ValueTask<AgentSession> RestoreOrCreateSessionAsync(AIAgent agent, Conversation conversation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(conversation.SessionState) || conversation.SessionState == "{}")
        {
            return await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        using var document = JsonDocument.Parse(conversation.SessionState);
        return await agent.DeserializeSessionAsync(document.RootElement, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
