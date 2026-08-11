using System.Text.Json;
using Chater.AI;
using Chater.AI.Conversations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

#pragma warning disable MAAI001

namespace Chater.Tests;

public sealed class AgentSessionRoundTripTests
{
    [Fact]
    public async Task SerializedHarnessSession_RestoresPreviousTurnIntoNextRequest()
    {
        var firstClient = new RecordingChatClient("reply-one");
        var firstAgent = CreateAgent(firstClient);
        var session = await firstAgent.CreateSessionAsync();

        await foreach (var _ in firstAgent.RunStreamingAsync("remember-me", session))
        {
        }

        JsonElement serialized = await firstAgent.SerializeSessionAsync(session);
        var secondClient = new RecordingChatClient("reply-two");
        var secondAgent = CreateAgent(secondClient);
        session = await secondAgent.DeserializeSessionAsync(serialized);

        await foreach (var _ in secondAgent.RunStreamingAsync("follow-up", session))
        {
        }

        var request = Assert.Single(secondClient.Requests);
        Assert.Contains(request, message => message.Role == ChatRole.User && message.Text == "remember-me");
        Assert.Contains(request, message => message.Role == ChatRole.Assistant && message.Text == "reply-one");
        Assert.Contains(request, message => message.Role == ChatRole.User && message.Text == "follow-up");
    }

    [Fact]
    public async Task RepairMissingHistory_RehydratesDurablePreviousTurns()
    {
        var agent = CreateAgent(new RecordingChatClient("unused"));
        var session = await agent.CreateSessionAsync();
        var historyProvider = Assert.IsType<InMemoryChatHistoryProvider>(agent.GetService<InMemoryChatHistoryProvider>());
        // Simulate the observed corrupt state: the prior user input survived but its assistant reply did not.
        historyProvider.SetMessages(session, [new ChatMessage(ChatRole.User, "remember-me")]);
        var now = DateTimeOffset.UtcNow;
        Message[] durableHistory =
        [
            new("u1", "conversation", 1, MessageRole.User, "remember-me", MessageStatus.Completed, null, null, now, now),
            new("a1", "conversation", 2, MessageRole.Assistant, "reply-one", MessageStatus.Completed, null, null, now, now)
        ];

        ChatService.RepairMissingHistory(agent, session, durableHistory);

        var repaired = historyProvider.GetMessages(session);
        Assert.Collection(
            repaired,
            message => { Assert.Equal(ChatRole.User, message.Role); Assert.Equal("remember-me", message.Text); },
            message => { Assert.Equal(ChatRole.Assistant, message.Role); Assert.Equal("reply-one", message.Text); });
    }

    [Fact]
    public async Task RepairMissingHistory_PreservesCompleteFrameworkHistory()
    {
        var agent = CreateAgent(new RecordingChatClient("unused"));
        var session = await agent.CreateSessionAsync();
        var historyProvider = Assert.IsType<InMemoryChatHistoryProvider>(agent.GetService<InMemoryChatHistoryProvider>());
        var frameworkHistory = new List<ChatMessage>
        {
            new(ChatRole.User, "remember-me"),
            new(ChatRole.Assistant, "rich-framework-reply"),
            new(ChatRole.System, "framework-state")
        };
        historyProvider.SetMessages(session, frameworkHistory);
        var now = DateTimeOffset.UtcNow;
        Message[] durableHistory =
        [
            new("u1", "conversation", 1, MessageRole.User, "remember-me", MessageStatus.Completed, null, null, now, now),
            new("a1", "conversation", 2, MessageRole.Assistant, "Used a tool.\nrich-framework-reply", MessageStatus.Completed, null, null, now, now)
        ];

        ChatService.RepairMissingHistory(agent, session, durableHistory);

        Assert.Same(frameworkHistory, historyProvider.GetMessages(session));
    }

    private static AIAgent CreateAgent(IChatClient client) => client.AsHarnessAgent(new HarnessAgentOptions
    {
        Name = "session-test",
        HarnessInstructions = string.Empty,
        DisableFileMemory = true,
        DisableAgentSkillsProvider = true,
        DisableWebSearch = true,
        DisableToolAutoApproval = true,
        DisableOpenTelemetry = true,
        MaxContextWindowTokens = 128_000,
        MaxOutputTokens = 16_384,
        MaximumIterationsPerRequest = 12
    });

    private sealed class RecordingChatClient(string reply) : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.Select(static message => message.Clone()).ToList());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.Select(static message => message.Clone()).ToList());
            yield return new ChatResponseUpdate(ChatRole.Assistant, reply) { MessageId = Guid.NewGuid().ToString("N") };
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}

#pragma warning restore MAAI001
