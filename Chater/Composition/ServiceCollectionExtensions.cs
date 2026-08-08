using Chater.AI;
using Chater.Data;
using Chater.Localization;
using Chater.Logging;
using Chater.Services;
using Chater.ViewModels;
using Chater.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chater.Composition;

/// <summary>Registers the application's composition root and performs required local-database startup work.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers all production services. An optional path override supports isolated tests.</summary>
    public static IServiceCollection AddChaterApplication(this IServiceCollection services, AppPaths? paths = null)
    {
        var appPaths = paths ?? AppPaths.CreateDefault();
        services.AddSingleton(appPaths);
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
        });
        services.AddSingleton<ILoggerProvider>(_ => new DailyFileLoggerProvider(appPaths.LogsDirectory));
        services.AddSingleton(static provider =>
        {
            var appPaths = provider.GetRequiredService<AppPaths>();
            appPaths.EnsureCreated();
            return new SqliteDatabase(appPaths.DatabasePath);
        });
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<MessageRepository>();
        services.AddSingleton<AppSettingRepository>();
        services.AddSingleton<StartupRecoveryService>();
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<AppState>();
        services.AddSingleton<IUpdateService>(provider => new UpdateService(provider.GetRequiredService<AppPaths>(), provider.GetRequiredService<AppState>()));
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<IConfirmationService, ConfirmationService>();
        services.AddSingleton<IWindowNavigationService, WindowNavigationService>();
        services.AddSingleton<IGlobalHotKeyService, GlobalHotKeyService>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsWindow>();

        services.AddChaterAi();
        return services;
    }

    /// <summary>
    /// Completes schema migration and interrupted-message recovery before any window reads application data.
    /// </summary>
    public static void InitializeChaterDatabase(this IServiceProvider services)
    {
        services.GetRequiredService<DatabaseMigrator>().MigrateAsync().GetAwaiter().GetResult();
        services.GetRequiredService<StartupRecoveryService>().RecoverAsync().GetAwaiter().GetResult();
    }
}
