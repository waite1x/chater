using Microsoft.Extensions.DependencyInjection;

namespace Chater.AI.Tools;

public static class ChaterToolExtensions
{
    public static IServiceCollection AddChaterTools(this IServiceCollection services)
    {
        services.AddSingleton<ChatToolRegistry>();
        AddInternalTools(services);
        return services;
    }

    private static void AddInternalTools(IServiceCollection services)
    {
        services.AddSingleton<WebContentTool>();
        services.AddSingleton<IChatToolProvider, DefaultToolProvider>();
    }
}