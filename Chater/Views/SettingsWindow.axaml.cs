using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Chater.Services;
using Chater.ViewModels;
using Chater;

namespace Chater.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsChatShortcut(e.Key, e.KeyModifiers))
        {
            viewModel.ShowChatCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && ShortcutFormatter.TryFormat(e.Key, e.KeyModifiers, out var shortcut))
        {
            viewModel.ChatShortcut = shortcut;
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Application.Current is not App { IsExiting: true })
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
