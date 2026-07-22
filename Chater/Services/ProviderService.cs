using Chater.Data;
using Chater.Models;
using Chater.Models.Enums;
using Chater.Providers;

namespace Chater.Services;

public sealed class ProviderService
{
    private readonly ApiProviderRepository _providers;
    private readonly IProviderConnectionTester? _connectionTester;

    public ProviderService(ApiProviderRepository providers, IProviderConnectionTester? connectionTester = null)
    {
        _providers = providers;
        _connectionTester = connectionTester;
    }

    public async Task SaveAsync(ApiProvider provider, CancellationToken cancellationToken = default)
    {
        Validate(provider);
        await _providers.SaveAsync(provider with { UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ApiProvider>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _providers.GetAllAsync(cancellationToken);

    public Task<ProviderConnectionResult> TestConnectionAsync(ApiProvider provider, CancellationToken cancellationToken = default)
    {
        Validate(provider);
        return (_connectionTester ?? throw new InvalidOperationException("Provider connection testing is not configured.")).TestAsync(provider, cancellationToken);
    }

    public static void Validate(ApiProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.ModelId);
        if (provider.ProviderType is not ProviderType.Ollama && string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            throw new ArgumentException("An API key is required for the selected provider.", nameof(provider));
        }

        if (provider.ProviderType is ProviderType.Ollama or ProviderType.OpenAiCompatible && !Uri.TryCreate(provider.Endpoint, UriKind.Absolute, out _))
        {
            throw new ArgumentException("A valid absolute endpoint is required for the selected provider.", nameof(provider));
        }
    }
}
