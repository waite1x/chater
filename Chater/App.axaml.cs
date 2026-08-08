using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Chater.Composition;
using Chater.Localization;
using Chater.Logging;
using Chater.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chater;

public partial class App : Application
{
    private const string DarkTrayIconUri = "avares://Chater/Assets/chater-tray.png";
    private const string LightTrayIconUri = "avares://Chater/Assets/chater-tray-light.png";
    private ServiceProvider? _services;
    private ILogger<App>? _logger;
    private IPlatformSettings? _platformSettings;
    private TrayIcon? _trayIcon;
    private bool _updateDialogOpen;
    internal bool IsExiting { get; private set; }
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _platformSettings = TryGetFeature(typeof(IPlatformSettings)) as IPlatformSettings;
        if (_platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged += (_, _) => UpdateTrayIcon();
        }

        _services = new ServiceCollection().AddChaterApplication().BuildServiceProvider();
        _logger = _services.GetRequiredService<ILogger<App>>();
        ExceptionLogger.Configure(_services.GetRequiredService<ILoggerFactory>());
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        _logger.LogInformation("Chater is starting");

        try
        {
            _services.InitializeChaterDatabase();
        }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "Chater failed during startup");
            throw;
        }
        ApplyStoredTheme(_services.GetRequiredService<AppSettingsService>());
        UpdateTrayIcon();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<Chater.Views.MainWindow>();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var viewModel = _services.GetRequiredService<Chater.ViewModels.MainWindowViewModel>();
            var updateService = _services.GetRequiredService<IUpdateService>();
            updateService.UpdateAvailable += OnUpdateAvailable;
            desktop.MainWindow.DataContext = viewModel;
            desktop.MainWindow.Opened += async (_, _) =>
            {
                await viewModel.LoadAsync();
                var globalHotKeys = _services.GetRequiredService<Services.IGlobalHotKeyService>();
                if (!globalHotKeys.Start(viewModel.ChatShortcut, viewModel.NewChatWindowShortcut) && globalHotKeys.LastError is not null)
                {
                    viewModel.StatusMessage = globalHotKeys.LastError;
                    _logger?.LogWarning("Global hotkey registration failed: {Error}", globalHotKeys.LastError);
                }
                _ = CheckForUpdatesAsync(updateService);
            };
            desktop.Exit += (_, _) =>
            {
                IsExiting = true;
                _logger?.LogInformation("Chater is shutting down");
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
                _services.GetRequiredService<Services.IGlobalHotKeyService>().Dispose();
                _services.Dispose();
                ExceptionLogger.Configure(null);
                _services = null;
                _logger = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyStoredTheme(AppSettingsService settings)
    {
        // Apply the persisted choice before any window is constructed. Loading it from the
        // MainWindow.Opened event lets the system theme render briefly on the first frame.
        var theme = settings.GetAsync(AppSettingsService.ThemeKey).GetAwaiter().GetResult()
            ?? AppSettingsService.DefaultTheme;
        AppSettingsService.ApplyTheme(theme);
    }

    private void UpdateTrayIcon()
    {
        _trayIcon ??= TrayIcon.GetIcons(this)?.FirstOrDefault();
        if (_trayIcon is null)
        {
            return;
        }

        // Native trays do not inherit Avalonia brushes. Use the OS theme rather than the application's selected theme.
        var systemTheme = _platformSettings?.GetColorValues().ThemeVariant;
        var iconUri = systemTheme == PlatformThemeVariant.Dark ? LightTrayIconUri : DarkTrayIconUri;
        using var stream = AssetLoader.Open(new Uri(iconUri));
        _trayIcon.Icon = new WindowIcon(new Bitmap(stream));
    }

    private async Task CheckForUpdatesAsync(IUpdateService updateService)
    {
        try
        {
            await updateService.CheckForUpdateAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "The update check failed");
            // Update checks are best-effort and must not interfere with startup.
        }
    }

    private void OnUpdateAvailable(object? sender, AppUpdateInfo update)
    {
        if (sender is IUpdateService updates)
            Dispatcher.UIThread.Post(() => _ = PromptAndDownloadUpdateAsync(updates, update));
    }

    private async Task PromptAndDownloadUpdateAsync(IUpdateService updates, AppUpdateInfo update)
    {
        if (!await ShowUpdateDialogAsync(updates, update, Chater.Views.UpdateDialogMode.Download))
            return;

        _ = DownloadUpdateAsync(updates, update);
    }

    private async Task DownloadUpdateAsync(IUpdateService updates, AppUpdateInfo update)
    {
        try
        {
            var downloadedFilePath = await updates.DownloadAsync(update).ConfigureAwait(false);
            if (Application.Current is App app)
                Dispatcher.UIThread.Post(() => _ = app.ShowUpdateDialogAsync(updates, update, Chater.Views.UpdateDialogMode.Install, downloadedFilePath));
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Downloading update {Version} failed", update.LatestVersion);
            // Download errors are published by UpdateService and shown in the About page.
        }
    }

    private async Task<bool> ShowUpdateDialogAsync(IUpdateService updates, AppUpdateInfo update, Chater.Views.UpdateDialogMode mode, string? downloadedFilePath = null)
    {
        if (_updateDialogOpen || _services is null || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return false;
        }

        var owner = desktop.Windows.FirstOrDefault(window => window.IsActive && window.IsVisible)
            ?? desktop.Windows.LastOrDefault(window => window.IsVisible)
            ?? desktop.MainWindow;
        if (owner is null || !owner.IsVisible)
        {
            return false;
        }

        _updateDialogOpen = true;
        var disabledWindows = desktop.Windows
            .Where(window => !ReferenceEquals(window, owner) && window.IsVisible && window.IsEnabled)
            .ToArray();
        try
        {
            foreach (var window in disabledWindows)
                window.IsEnabled = false;

            var localization = _services.GetRequiredService<LocalizationService>();
            var dialog = new Chater.Views.UpdateDialog(update, localization, mode);
            dialog.Opened += (_, _) =>
            {
                dialog.Activate();
                dialog.Focus();
            };
            var confirmed = await dialog.ShowDialog<bool>(owner);
            if (confirmed && mode == Chater.Views.UpdateDialogMode.Install && downloadedFilePath is not null)
            {
                updates.LaunchInstaller(downloadedFilePath);
                desktop.Shutdown();
            }
            return confirmed;
        }
        finally
        {
            foreach (var window in disabledWindows)
                window.IsEnabled = true;
            _updateDialogOpen = false;
        }
    }

    private void OnTrayShowChat(object? sender, EventArgs e) => _services?.GetRequiredService<Services.IWindowNavigationService>().ShowChat();

    private void OnTrayShowSettings(object? sender, EventArgs e) => _services?.GetRequiredService<Services.IWindowNavigationService>().ShowSettings();

    private void OnTrayExit(object? sender, EventArgs e)
    {
        IsExiting = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        if (eventArgs.ExceptionObject is Exception exception)
        {
            _logger?.LogCritical(exception, "An unhandled application exception occurred; terminating: {IsTerminating}", eventArgs.IsTerminating);
        }
        else
        {
            _logger?.LogCritical("An unhandled non-Exception error occurred; terminating: {IsTerminating}", eventArgs.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        _logger?.LogError(eventArgs.Exception, "An unobserved task exception occurred");
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        _logger?.LogCritical(eventArgs.Exception, "An unhandled UI-thread exception occurred");
    }
}
