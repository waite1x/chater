using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chater.AI.Providers;
using Chater.Localization;
using Chater.Logging;
using Chater.Services;

namespace Chater.ViewModels;

public sealed partial class ApiKeySettingsViewModel : SettingsViewModelBase
{
    private readonly ProviderService _providerService;
    private readonly IConfirmationService? _confirmation;

    public ApiKeySettingsViewModel(
        ProviderService providerService,
        LocalizationService localization,
        IConfirmationService? confirmation = null)
        : base(localization)
    {
        _providerService = providerService;
        _confirmation = confirmation;
    }

    public ObservableCollection<ApiProvider> Providers { get; } = [];
    public IReadOnlyList<ProviderType> ProviderTypes { get; } = Enum.GetValues<ProviderType>();
    public ObservableCollection<string> FetchedModels { get; } = [];

    [ObservableProperty]
    private ApiProvider? _selectedProvider;

    [ObservableProperty]
    private string _providerName = string.Empty;

    [ObservableProperty]
    private ProviderType _providerType = ProviderType.OpenAi;

    [ObservableProperty]
    private string _providerModelId = string.Empty;

    [ObservableProperty]
    private string _providerEndpoint = string.Empty;

    [ObservableProperty]
    private string _providerApiKey = string.Empty;

    [ObservableProperty]
    private bool _isFetchingModels;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Providers.Clear();
        foreach (var provider in await _providerService.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (provider.IsEnabled)
            {
                Providers.Add(provider);
            }
        }

        if (SelectedProvider is null && Providers.Count > 0)
        {
            SelectedProvider = Providers.FirstOrDefault(p => p.IsDefault) ?? Providers[0];
        }
    }

    [RelayCommand]
    private void AddProvider()
    {
        SelectedProvider = null;
        ProviderName = string.Empty;
        ProviderType = ProviderType.OpenAi;
        ProviderModelId = string.Empty;
        ProviderEndpoint = string.Empty;
        ProviderApiKey = string.Empty;
        FetchedModels.Clear();
        StatusMessage = T("AddingProvider");
    }

    [RelayCommand]
    private async Task SaveProviderAsync()
    {
        var provider = BuildEditedProvider();
        try
        {
            await _providerService.SaveAsync(provider).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
            SelectedProvider = Providers.FirstOrDefault(item => item.Id == provider.Id);
            StatusMessage = T("ProviderSaved");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ApiKeySettingsViewModel), "Failed to save provider");
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteProviderAsync(ApiProvider? provider)
    {
        if (provider is null) return;

        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(provider.Name))
            return;

        try
        {
            await _providerService.DeleteAsync(provider.Id).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
            StatusMessage = T("ProviderDeleted");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ApiKeySettingsViewModel), "Failed to delete provider");
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task FetchModelsAsync()
    {
        if (IsFetchingModels) return;
        IsFetchingModels = true;
        try
        {
            var provider = BuildEditedProvider();
            if (provider.ProviderType == ProviderType.Anthropic)
            {
                StatusMessage = T("FetchModelsNotSupported");
                return;
            }

            StatusMessage = T("FetchingModels");
            FetchedModels.Clear();
            var models = await _providerService.FetchModelsAsync(provider).ConfigureAwait(false);
            foreach (var model in models)
                FetchedModels.Add(model);

            StatusMessage = string.Format(T("FetchedModelsCount"), FetchedModels.Count);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ApiKeySettingsViewModel), "Failed to fetch models");
            StatusMessage = exception.Message;
        }
        finally
        {
            IsFetchingModels = false;
        }
    }

    [RelayCommand]
    private void AddFetchedModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return;
        var current = ProviderModelId
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (current.Add(modelId.Trim()))
        {
            ProviderModelId = string.Join(Environment.NewLine, current);
        }
    }

    private ApiProvider BuildEditedProvider()
    {
        var existing = SelectedProvider;
        var now = DateTimeOffset.UtcNow;
        var modelIds = ProviderModelId
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activeModel = modelIds.FirstOrDefault() ?? string.Empty;
        return new ApiProvider(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(ProviderName) ? (existing?.Name ?? "Unnamed") : ProviderName.Trim(),
            ProviderType,
            string.IsNullOrWhiteSpace(ProviderApiKey) ? existing?.ApiKey ?? string.Empty : ProviderApiKey,
            string.IsNullOrWhiteSpace(ProviderEndpoint) ? null : ProviderEndpoint.Trim(),
            activeModel,
            existing?.IsDefault ?? Providers.Count == 0,
            true,
            existing?.CreatedAt ?? now,
            now) with { ModelIds = modelIds };
    }

    partial void OnSelectedProviderChanged(ApiProvider? value)
    {
        FetchedModels.Clear();
        if (value is null) return;

        ProviderName = value.Name;
        ProviderType = value.ProviderType;
        ProviderModelId = string.Join(Environment.NewLine, value.ModelIds);
        ProviderEndpoint = value.Endpoint ?? string.Empty;
        ProviderApiKey = string.Empty;
    }
}
