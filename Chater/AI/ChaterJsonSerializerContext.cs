using System.Text.Json;
using System.Text.Json.Serialization;
using Chater.AI.Conversations;
using Chater.Services;
using Microsoft.Agents.AI;

namespace Chater.AI;

[JsonSerializable(typeof(ProviderSnapshot))]
[JsonSerializable(typeof(UpdateService.GitHubRelease))]
[JsonSerializable(typeof(MessageAttachment[]))]
[JsonSerializable(typeof(AgentRequestMessageSourceAttribution))]
internal sealed partial class ChaterJsonSerializerContext : JsonSerializerContext;

internal static class ChaterJsonSerializerOptions
{
    public static JsonSerializerOptions AgentSession { get; } = CreateAgentSessionOptions();

    private static JsonSerializerOptions CreateAgentSessionOptions()
    {
        var options = new JsonSerializerOptions(AgentAbstractionsJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Add(ChaterJsonSerializerContext.Default);
        options.MakeReadOnly();
        return options;
    }
}
