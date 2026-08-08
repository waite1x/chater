using System.Text.Json.Serialization;
using Chater.AI.Conversations;
using Chater.Services;

namespace Chater.AI;

[JsonSerializable(typeof(ProviderSnapshot))]
[JsonSerializable(typeof(UpdateService.GitHubRelease))]
internal sealed partial class ChaterJsonSerializerContext : JsonSerializerContext;
