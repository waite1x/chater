using Chater.AI.Conversations;
using Chater.AI.Providers;
using Chater.AI.Skills;
using Chater.AI.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Chater.AI;

public static class ChaterAiExtensions
{
    public static IServiceCollection AddChaterAi(this IServiceCollection services)
    {
        services.AddScoped<ChatService>()
            .AddChaterTools()
            .AddChaterAiConversations()
            .AddChaterAiProviders()
            .AddChaterSkills();
        
        return services;
    }
}
