using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Chater.ViewModels;

namespace Chater.Views;

public partial class MainWindow : Window
{
    protected override bool HideOnClose => false;

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
