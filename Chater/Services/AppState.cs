using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chater.Logging;

namespace Chater.Services;

/// <summary>
/// Process-wide state shared by chat and settings windows.
/// Persistent settings are still stored by AppSettingsService; this object is
/// the live source of truth while the application is running.
/// </summary>
public sealed partial class AppState : ObservableObject
{
    private Func<CancellationToken, Task<AppUpdateInfo?>>? _checkForUpdates;

    public AppState()
    {
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
    }

    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    [ObservableProperty]
    private string _themeKey = AppSettingsService.DefaultTheme;

    [ObservableProperty]
    private string _accentColorHex = AppSettingsService.DefaultAccentColor;

    [ObservableProperty]
    private string _languageKey = AppSettingsService.DefaultLanguage;

    [ObservableProperty]
    private string _chatShortcut = AppSettingsService.DefaultChatShortcut;

    [ObservableProperty]
    private string _newChatWindowShortcut = AppSettingsService.DefaultNewChatWindowShortcut;

    [ObservableProperty]
    private bool _launchAtStartup;

    [ObservableProperty]
    private UpdateProgress _updateProgress = new(UpdateState.Idle);

    [ObservableProperty]
    private string _currentVersion = "0.0.0";

    public bool SettingsLoaded { get; set; }

    public bool CanCheckForUpdates => UpdateProgress.State is not (UpdateState.Checking or UpdateState.Downloading);

    internal void ConfigureUpdateChecker(Func<CancellationToken, Task<AppUpdateInfo?>> checkForUpdates)
    {
        _checkForUpdates = checkForUpdates;
    }

    partial void OnUpdateProgressChanged(UpdateProgress value)
    {
        OnPropertyChanged(nameof(CanCheckForUpdates));
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_checkForUpdates is not null)
        {
            try
            {
                await _checkForUpdates(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ExceptionLogger.Log(exception, nameof(AppState), "Update command failed");
                // UpdateService publishes the failure through UpdateProgress.
            }
        }
    }
}
