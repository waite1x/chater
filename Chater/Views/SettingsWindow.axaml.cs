using Avalonia.Controls;
using Avalonia.Input;
using Chater.ViewModels;
using Chater.Views.Settings;
using System.ComponentModel;

namespace Chater.Views;

public partial class SettingsWindow : Window
{
    protected override bool HideOnClose => false;
    private MainWindowViewModel? _viewModel;
    private Control? _currentPage;
    private bool _updatingNavigation;

    public SettingsWindow()
    {
        InitializeComponent();
        ConfigurePlatformTitleBar();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ShowPage(_viewModel.SelectedSettingsPageKey);
        }
        else
        {
            DisposeCurrentPage();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedSettingsPageKey) && _viewModel is not null)
        {
            ShowPage(_viewModel.SelectedSettingsPageKey);
        }
    }

    private void OnSettingsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingNavigation || _viewModel is null || SettingsNavigation.SelectedItem is not ListBoxItem { Tag: string pageKey })
            return;

        _viewModel.SelectSettingsPage(pageKey);
    }

    private void ShowPage(string pageKey)
    {
        var selectedItem = SettingsNavigation.ItemsView.OfType<ListBoxItem>().FirstOrDefault(item => Equals(item.Tag, pageKey));
        _updatingNavigation = true;
        try { SettingsNavigation.SelectedItem = selectedItem; }
        finally { _updatingNavigation = false; }

        DisposeCurrentPage();
        if (_viewModel is null) return;

        _currentPage = pageKey switch
        {
            MainWindowViewModel.GeneralSettingsPage => new GeneralSettingsView(),
            MainWindowViewModel.ApiKeySettingsPage => new ApiKeySettingsView(),
            MainWindowViewModel.SkillsSettingsPage => new SkillSettingsView(),
            MainWindowViewModel.ShortcutSettingsPage => new ShortcutSettingsView(),
            MainWindowViewModel.HistorySettingsPage => new HistorySettingsView(),
            MainWindowViewModel.AboutSettingsPage => new AboutSettingsView(),
            _ => new GeneralSettingsView()
        };
        _currentPage.DataContext = _viewModel;
        SettingsContent.Content = _currentPage;
    }

    private void DisposeCurrentPage()
    {
        SettingsContent.Content = null;
        if (_currentPage is IDisposable disposable)
            disposable.Dispose();
        _currentPage = null;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.IsChatShortcut(e.Key, e.KeyModifiers))
        {
            viewModel.ShowChatCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        DisposeCurrentPage();
        base.OnClosed(e);
    }

}
