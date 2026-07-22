using Avalonia.Controls;
using Chater.ViewModels;

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
}
