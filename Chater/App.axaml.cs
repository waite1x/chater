using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _services = new ServiceCollection().AddChaterApplication().BuildServiceProvider();
        _services.InitializeChaterDatabase();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<Chater.Views.MainWindow>();
            var viewModel = _services.GetRequiredService<Chater.ViewModels.MainWindowViewModel>();
            desktop.MainWindow.DataContext = viewModel;
            desktop.MainWindow.Opened += async (_, _) => await viewModel.LoadAsync();
            desktop.Exit += (_, _) => _services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
