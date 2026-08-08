using Avalonia;
using Chater.ViewModels;
using Chater.Views;
using Chater.Logging;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;

namespace Chater.Services;

/// <summary>
/// Coordinates top-level windows while keeping their view-model lifetime separate from the UI controls.
/// </summary>
public sealed class WindowNavigationService(IServiceProvider services) : IWindowNavigationService
{
    // Settings is intentionally single-instance; chat windows may have independent conversations.
    private SettingsWindow? _settingsWindow;
    private MainWindowViewModel? _settingsViewModel;

    public void ShowSettings()
    {
        ShowSettings(MainWindowViewModel.GeneralSettingsPage, 0);
    }

    public void ShowSkillSettings()
    {
        ShowSettings(MainWindowViewModel.SkillsSettingsPage, 1);
    }

    /// <summary>Shows the existing chat window, or creates one when none exists.</summary>
    public void ShowChat()
    {
        if (FindFirstChatWindow() is { } existingWindow)
        {
            ActivateChatWindow(existingWindow);
            return;
        }

        ShowChat(null);
    }

    /// <summary>Always creates a new chat window, even when another chat window is already open.</summary>
    public void ShowNewChat() => ShowChat(null);

    public void ShowChat(string? conversationId)
    {
        var window = services.GetRequiredService<MainWindow>();
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.DataContext = viewModel;
        window.Closed += OnChatWindowClosed;
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        window.Focus();
        _ = InitializeChatWindowAsync(viewModel, conversationId);
    }

    private static MainWindow? FindFirstChatWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
        ?.Windows.OfType<MainWindow>()
        .FirstOrDefault();

    private static void ActivateChatWindow(MainWindow window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        window.WindowState = WindowState.Normal;
        window.Activate();
        window.Focus();
    }

    private void ShowSettings(string pageKey, int compatibilityIndex)
    {
        var window = _settingsWindow ??= CreateSettingsWindow();
        var isNewViewModel = _settingsViewModel is null;
        _settingsViewModel ??= services.GetRequiredService<MainWindowViewModel>();
        var viewModel = _settingsViewModel;
        viewModel.SelectSettingsPage(pageKey);
        window.DataContext = viewModel;
        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
        window.Focus();

        if (isNewViewModel)
        {
            _ = InitializeSettingsWindowAsync(viewModel);
        }
    }

    private SettingsWindow CreateSettingsWindow()
    {
        var window = services.GetRequiredService<SettingsWindow>();
        window.Closed += OnSettingsWindowClosed;
        return window;
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        if (sender is SettingsWindow window && ReferenceEquals(window, _settingsWindow))
        {
            window.DataContext = null;
            _settingsViewModel?.Dispose();
            _settingsViewModel = null;
            _settingsWindow = null;
        }
    }

    private async Task InitializeChatWindowAsync(MainWindowViewModel viewModel, string? conversationId)
    {
        try
        {
            // The window is shown first so long-running I/O never delays native window creation.
            await viewModel.LoadAsync().ConfigureAwait(false);
            if (conversationId is not null)
                await viewModel.OpenConversationAsync(conversationId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(WindowNavigationService), "Failed to initialize a chat window");
            viewModel.StatusMessage = exception.Message;
        }
    }

    private static async Task InitializeSettingsWindowAsync(MainWindowViewModel viewModel)
    {
        try
        {
            await viewModel.LoadAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(WindowNavigationService), "Failed to initialize the settings window");
            viewModel.StatusMessage = exception.Message;
        }
    }

    private static void OnChatWindowClosed(object? sender, EventArgs e)
    {
        if (sender is MainWindow window)
        {
            window.Closed -= OnChatWindowClosed;
            window.DataContext = null;
        }
    }
}
