using Microsoft.Extensions.DependencyInjection;

namespace Chater.AI.Tools;

public static class ChaterToolExtensions
{
    public static IServiceCollection AddChaterTools(this IServiceCollection services)
    {
        services.AddScoped<ChatToolRegistry>();
        AddInternalTools(services);
        return services;
    }

    private static void AddInternalTools(IServiceCollection services)
    {
        services.AddScoped<WebContentTool>();
        services.AddScoped<IChatToolProvider, DefaultToolProvider>();
    }
}