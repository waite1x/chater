using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Chater.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Chater;

public partial class App : Application
{
    private ServiceProvider? _services;
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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<Chater.Views.MainWindow>();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var viewModel = _services.GetRequiredService<Chater.ViewModels.MainWindowViewModel>();
            desktop.MainWindow.DataContext = viewModel;
            desktop.MainWindow.Opened += async (_, _) =>
            {
                await viewModel.LoadAsync();
                var globalHotKeys = _services.GetRequiredService<Services.IGlobalHotKeyService>();
                if (!globalHotKeys.Start(viewModel.ChatShortcut) && globalHotKeys.LastError is not null)
                {
                    viewModel.StatusMessage = globalHotKeys.LastError;
                }
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
