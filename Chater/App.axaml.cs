using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Chater.Composition;
using Chater.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Chater;

public partial class App : Application
{
    private ServiceProvider? _services;
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
        _services = new ServiceCollection().AddChaterApplication().BuildServiceProvider();
        _services.InitializeChaterDatabase();
        ApplyStoredTheme(_services.GetRequiredService<AppSettingsService>());

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
                }
                _ = CheckForUpdatesAsync(updateService);
            };
            desktop.Exit += (_, _) =>
            {
                IsExiting = true;
                _services.GetRequiredService<Services.IGlobalHotKeyService>().Dispose();
                _services.Dispose();
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

    private static async Task CheckForUpdatesAsync(IUpdateService updateService)
    {
        try
        {
            await updateService.CheckForUpdateAsync().ConfigureAwait(false);
        }
        catch
        {
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

    private static async Task DownloadUpdateAsync(IUpdateService updates, AppUpdateInfo update)
    {
        try
        {
            var downloadedFilePath = await updates.DownloadAsync(update).ConfigureAwait(false);
            if (Application.Current is App app)
                Dispatcher.UIThread.Post(() => _ = app.ShowUpdateDialogAsync(updates, update, Chater.Views.UpdateDialogMode.Install, downloadedFilePath));
        }
        catch
        {
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
}
