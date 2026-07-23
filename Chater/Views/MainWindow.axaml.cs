using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Chater.ViewModels;
using Chater;

namespace Chater.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnDraftTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && sender is TextBox textBox)
        {
            viewModel.Draft = textBox.Text ?? string.Empty;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
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
