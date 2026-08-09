using Microsoft.Extensions.DependencyInjection;

namespace Chater.AI.Conversations;

public static class ChaterAiConversationExtensions
{
    public static IServiceCollection AddChaterAiConversations(this IServiceCollection services)
    {
        services.AddScoped<ConversationRepository>()
            .AddScoped<ConversationService>()
            .AddSingleton<SessionRunLock>();
        return services;
    }
}