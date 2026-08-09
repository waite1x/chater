using Chater.AI.Providers;
using Chater.Data;
using Chater.Localization;
using Chater.Services;
using Chater.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Chater.Tests;

public sealed class ApiKeySettingsViewModelTests
{
    [Fact]
    public void SelectedProvider_PopulatesModelRowsWithMultimodalFlags()
    {
        var viewModel = CreateViewModel();
        var provider = new ApiProvider("p", "Provider", ProviderType.OpenAi, "key", null, "model-a", true, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        {
            ModelIds = ["model-a", "model-b"],
            MultimodalModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "model-b" }
        };

        viewModel.SelectedProvider = provider;

        Assert.Equal(2, viewModel.ProviderModels.Count);
        Assert.Equal("model-a", viewModel.ProviderModels[0].ModelId);
        Assert.False(viewModel.ProviderModels[0].IsMultimodal);
        Assert.Equal("model-b", viewModel.ProviderModels[1].ModelId);
        Assert.True(viewModel.ProviderModels[1].IsMultimodal);
    }

    [Fact]
    public void BuildModelLists_MapsRowsToModelIdsAndMultimodalSet()
    {
        var rows = new[]
        {
            new ProviderModelItem("model-a", false),
            new ProviderModelItem("model-b", true),
            new ProviderModelItem("", true),
            new ProviderModelItem("model-a", true)
        };

        var (modelIds, multimodal) = ApiKeySettingsViewModel.BuildModelLists(rows);

        Assert.Equal(["model-a", "model-b"], modelIds);
        Assert.Contains("model-b", multimodal);
        Assert.Contains("model-a", multimodal);
    }

    [Fact]
    public void AddAndRemoveModel_UpdatesRows()
    {
        var viewModel = CreateViewModel();
        viewModel.ProviderModels.Add(new ProviderModelItem("model-a", false));

        viewModel.AddModelCommand.Execute(null);
        Assert.Equal(2, viewModel.ProviderModels.Count);

        viewModel.RemoveModelCommand.Execute(viewModel.ProviderModels[1]);
        Assert.Single(viewModel.ProviderModels);
    }

    private static ApiKeySettingsViewModel CreateViewModel()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var appState = new AppState(new LazyServiceProvider(services));
        var repository = new ApiProviderRepository(new SqliteDatabase(Path.Combine(Path.GetTempPath(), "Chater.Tests", $"{Guid.NewGuid():N}.db")));
        return new ApiKeySettingsViewModel(new ProviderService(repository), appState, new LocalizationService());
    }
}
