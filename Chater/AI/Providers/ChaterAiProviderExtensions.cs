using Microsoft.Extensions.DependencyInjection;

namespace Chater.AI.Providers;

public static class ChaterAiProviderExtensions
{
    public static IServiceCollection AddChaterAiProviders(this IServiceCollection services)
    {
        services.AddSingleton<ApiProviderRepository>();
        services.AddSingleton<IProviderConnectionTester, ProviderConnectionTester>();
        services.AddSingleton<ProviderService>();
        
        return services;
    }
}