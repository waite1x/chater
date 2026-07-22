using Chater.Models.Enums;

namespace Chater.Models;

public sealed record ApiProvider(
    string Id,
    string Name,
    ProviderType ProviderType,
    string ApiKey,
    string? Endpoint,
    string ModelId,
    bool IsDefault,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    // ModelId remains the active model for backwards compatibility. ModelIds
    // contains all models that share this provider/API key.
    public IReadOnlyList<string> ModelIds { get; init; } = [ModelId];
    public string ModelSummary => string.Join(", ", ModelIds);
}
