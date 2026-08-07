using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Chater.ViewModels;

namespace Chater.Views;

public partial class MainWindow : Window
{
    protected override bool HideOnClose => false;
    private MenuItem? _openProviderSubmenu;

    public MainWindow()
    {
        InitializeComponent();
        DraftTextBox.AddHandler(KeyDownEvent, OnDraftKeyDown, RoutingStrategies.Tunnel);
        ConfigurePlatformTitleBar();
    }

    private void OnDraftTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && sender is TextBox textBox)
        {
            viewModel.Draft = textBox.Text ?? string.Empty;
        }
    }

    private void OnDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel && viewModel.SendCommand.CanExecute(null))
        {
            viewModel.SendCommand.Execute(null);
        }

        // Prevent TextBox from inserting a newline and prevent the window's
        // default button handling from processing the same key press.
        e.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (DataContext is MainWindowViewModel viewModel && viewModel.IsChatShortcut(e.Key, e.KeyModifiers))
        {
            viewModel.ShowChatCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnModelMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: ModelMenuItem model })
        {
            model.SelectCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ensures only one provider submenu is open at a time. When a provider-level
    /// MenuItem opens its submenu, all sibling provider submenus are closed.
    /// </summary>
    private void OnProviderSubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem openedItem)
        {
            return;
        }

        // Keep the realized control rather than inspecting ItemsSource. Items
        // contains provider data objects, while the submenu itself is created
        // later by the item template.
        if (_openProviderSubmenu is { } previous && previous != openedItem)
        {
            previous.IsSubMenuOpen = false;
        }

        _openProviderSubmenu = openedItem;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Dispose();
            DataContext = null;
        }
        base.OnClosed(e);
    }

}
