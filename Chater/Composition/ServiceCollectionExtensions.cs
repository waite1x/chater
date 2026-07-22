using Chater.Data;
using Chater.Services;
using Chater.Providers;
using Chater.ViewModels;
using Chater.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Chater.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChaterApplication(this IServiceCollection services, AppPaths? paths = null)
    {
        services.AddSingleton(paths ?? AppPaths.CreateDefault());
        services.AddSingleton(static provider =>
        {
            var appPaths = provider.GetRequiredService<AppPaths>();
            appPaths.EnsureCreated();
            return new SqliteDatabase(appPaths.DatabasePath);
        });
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<MessageRepository>();
        services.AddSingleton<ApiProviderRepository>();
        services.AddSingleton<SkillRepository>();
        services.AddSingleton<ConversationRepository>();
        services.AddSingleton<StartupRecoveryService>();
        services.AddSingleton<IProviderConnectionTester, ProviderConnectionTester>();
        services.AddSingleton<ProviderService>();
        services.AddSingleton<SkillService>();
        services.AddSingleton<ConversationService>();
        services.AddSingleton<IWindowNavigationService, WindowNavigationService>();
        services.AddSingleton<SessionRunLock>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsWindow>();
        return services;
    }

    public static void InitializeChaterDatabase(this IServiceProvider services)
    {
        services.GetRequiredService<DatabaseMigrator>().MigrateAsync().GetAwaiter().GetResult();
        services.GetRequiredService<StartupRecoveryService>().RecoverAsync().GetAwaiter().GetResult();
    }
}
