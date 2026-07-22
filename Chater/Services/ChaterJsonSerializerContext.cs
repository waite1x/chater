using System.Text.Json.Serialization;

namespace Chater.Services;

[JsonSerializable(typeof(ProviderSnapshot))]
internal sealed partial class ChaterJsonSerializerContext : JsonSerializerContext;
