using Microsoft.Extensions.DependencyInjection;

namespace Chater.AI.Skills;

public static class ChaterAiSkillsExtensions
{
    public static IServiceCollection AddChaterSkills(this IServiceCollection services)
    {
        services.AddScoped<SkillRepository>()
            .AddScoped<SkillService>();

        return services;
    }
}