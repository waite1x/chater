using Chater.Logging;
using Chater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Chater.ViewModels;

/// <summary>
/// Process-wide state shared by chat and settings windows.
/// Persistent settings are still stored by AppSettingsService; this object is
/// the live source of truth while the application is running.
/// </summary>
public sealed partial class AppState : ObservableObject
{
    private readonly LazyServiceProvider _lazyServiceProvider;
    private Func<CancellationToken, Task<AppUpdateInfo?>>? _checkForUpdates;

    public AppState(LazyServiceProvider lazyServiceProvider)
    {
        _lazyServiceProvider = lazyServiceProvider;
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
    }

    private AppSettingsService AppSettingsService => _lazyServiceProvider.GetRequiredService<AppSettingsService>();
    
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    [ObservableProperty] private string _themeKey = AppSettingsService.DefaultTheme;

    [ObservableProperty] private string _accentColorHex = AppSettingsService.DefaultAccentColor;

    [ObservableProperty] private string _languageKey = AppSettingsService.DefaultLanguage;

    [ObservableProperty] private string _chatShortcut = AppSettingsService.DefaultChatShortcut;

    [ObservableProperty] private string _newChatWindowShortcut = AppSettingsService.DefaultNewChatWindowShortcut;

    [ObservableProperty] private bool _launchAtStartup;

    [ObservableProperty] private UpdateProgress _updateProgress = new(UpdateState.Idle);

    [ObservableProperty] private string _currentVersion = "0.0.0";

    public bool SettingsLoaded { get; set; }

    public bool CanCheckForUpdates => UpdateProgress.State is not (UpdateState.Checking or UpdateState.Downloading);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        await LoadAiStateAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void ConfigureUpdateChecker(Func<CancellationToken, Task<AppUpdateInfo?>> checkForUpdates)
    {
        _checkForUpdates = checkForUpdates;
    }

    partial void OnUpdateProgressChanged(UpdateProgress value)
    {
        OnPropertyChanged(nameof(CanCheckForUpdates));
    }

    private async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!SettingsLoaded)
        {
            ThemeKey = await AppSettingsService.GetAsync(AppSettingsService.ThemeKey, cancellationToken)
                                  .ConfigureAwait(false)
                              ?? AppSettingsService.DefaultTheme;
            LanguageKey = await AppSettingsService.GetAsync(AppSettingsService.LanguageKey, cancellationToken)
                                     .ConfigureAwait(false)
                                 ?? AppSettingsService.DefaultLanguage;
            ChatShortcut = await AppSettingsService.GetAsync(AppSettingsService.ChatShortcutKey, cancellationToken)
                                      .ConfigureAwait(false)
                                  ?? AppSettingsService.DefaultChatShortcut;
            NewChatWindowShortcut = await AppSettingsService
                                               .GetAsync(AppSettingsService.NewChatWindowShortcutKey, cancellationToken)
                                               .ConfigureAwait(false)
                                           ?? AppSettingsService.DefaultNewChatWindowShortcut;
            SettingsLoaded = true;
        }
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