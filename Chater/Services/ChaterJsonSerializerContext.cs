using System.Text.Json.Serialization;

namespace Chater.Services;

[JsonSerializable(typeof(ProviderSnapshot))]
[JsonSerializable(typeof(UpdateService.GitHubRelease))]
internal sealed partial class ChaterJsonSerializerContext : JsonSerializerContext;
