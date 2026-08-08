using Avalonia.Controls;
using Avalonia.Interactivity;
using Chater.ViewModels;

namespace Chater.Views.Settings;

public partial class ApiKeySettingsView : UserControl
{
    public ApiKeySettingsView() => InitializeComponent();

    private void OnFetchedModelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string modelId } && DataContext is MainWindowViewModel vm)
        {
            vm.AddFetchedModelCommand.Execute(modelId);
        }
    }
}
