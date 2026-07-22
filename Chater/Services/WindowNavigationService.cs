using Chater.ViewModels;
using Chater.Views;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Chater.Services;

public sealed class WindowNavigationService(IServiceProvider services) : IWindowNavigationService
{
    public void ShowSettings()
    {
        ShowSettings(0);
    }

    public void ShowSkillSettings()
    {
        ShowSettings(1);
    }

    public void ShowChat()
    {
        var window = services.GetRequiredService<MainWindow>();
        if (!window.IsVisible) window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        window.Focus();
    }

    private void ShowSettings(int tabIndex)
    {
        var window = services.GetRequiredService<SettingsWindow>();
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        viewModel.SettingsTabIndex = tabIndex;
        window.DataContext = viewModel;
        window.Show();
    }
}
