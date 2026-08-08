using Microsoft.Extensions.DependencyInjection;

namespace Chater.AI.Conversations;

public static class ChaterAiConversationExtensions
{
    public static IServiceCollection AddChaterAiConversations(this IServiceCollection services)
    {
        services.AddSingleton<ConversationRepository>()
            .AddSingleton<ConversationService>()
            .AddSingleton<SessionRunLock>();
        return services;
    }
}