using Chater.ViewModels;
using Chater.Views;
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

    private void ShowSettings(int tabIndex)
    {
        var window = services.GetRequiredService<SettingsWindow>();
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        viewModel.SettingsTabIndex = tabIndex;
        window.DataContext = viewModel;
        window.Show();
    }
}
