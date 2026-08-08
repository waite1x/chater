using Avalonia.Controls;
using Avalonia.Input;
using Chater.ViewModels;
using Chater.Views.Settings;
using Microsoft.Extensions.DependencyInjection;
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

        if (App.Current is not App app || app.Services is null) return;

        (_currentPage, var pageVm) = pageKey switch
        {
            MainWindowViewModel.GeneralSettingsPage => CreatePage<GeneralSettingsView, GeneralSettingsViewModel>(app.Services, vm => vm.LoadAsync()),
            MainWindowViewModel.ApiKeySettingsPage => CreatePage<ApiKeySettingsView, ApiKeySettingsViewModel>(app.Services, vm => vm.LoadAsync()),
            MainWindowViewModel.SkillsSettingsPage => CreatePage<SkillSettingsView, SkillSettingsViewModel>(app.Services, vm => vm.LoadAsync()),
            MainWindowViewModel.ShortcutSettingsPage => CreatePage<ShortcutSettingsView, ShortcutSettingsViewModel>(app.Services, vm => { vm.LoadFromState(); return Task.CompletedTask; }),
            MainWindowViewModel.HistorySettingsPage => CreatePage<HistorySettingsView, HistorySettingsViewModel>(app.Services, _ => Task.CompletedTask),
            MainWindowViewModel.AboutSettingsPage => CreatePage<AboutSettingsView, AboutSettingsViewModel>(app.Services, vm => { vm.LoadFromState(); return Task.CompletedTask; }),
            _ => CreatePage<GeneralSettingsView, GeneralSettingsViewModel>(app.Services, vm => vm.LoadAsync())
        };

        if (_currentPage is not null)
            SettingsContent.Content = _currentPage;
    }

    private static (Control?, SettingsViewModelBase?) CreatePage<TView, TVm>(IServiceProvider services, Func<TVm, Task> initialize)
        where TView : Control, new()
        where TVm : SettingsViewModelBase
    {
        var view = new TView();
        var vm = services.GetRequiredService<TVm>();
        view.DataContext = vm;
        _ = initialize(vm);
        return (view, vm);
    }

    private void DisposeCurrentPage()
    {
        if (_currentPage?.DataContext is IDisposable disposable)
            disposable.Dispose();
        SettingsContent.Content = null;
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
