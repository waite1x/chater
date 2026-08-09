using Microsoft.Extensions.DependencyInjection;

namespace Chater.AI.Providers;

public static class ChaterAiProviderExtensions
{
    public static IServiceCollection AddChaterAiProviders(this IServiceCollection services)
    {
        services.AddScoped<ApiProviderRepository>();
        services.AddScoped<IProviderConnectionTester, ProviderConnectionTester>();
        services.AddScoped<ProviderService>();
        
        return services;
    }
}