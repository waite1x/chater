using Chater.Localization;

namespace Chater.ViewModels;

public sealed class ToolSettingsViewModel(AppState appState, LocalizationService localization)
    : SettingsViewModelBase(localization)
{
    public AppState AppState { get; } = appState;

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        AppState.EnsureToolsInitialized();
        return Task.CompletedTask;
    }
}
