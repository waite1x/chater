using Chater.Composition;
using Chater.AI.Tools;
using Chater.Data;
using Chater.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Chater.Tests;

public sealed class CompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Chater.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void InitializeChaterDatabase_RegistersAndInitializesDataServices()
    {
        using var services = new ServiceCollection().AddChaterApplication(new AppPaths(_root)).BuildServiceProvider();

        services.InitializeChaterDatabase();

        Assert.NotNull(services.GetRequiredService<SqliteDatabase>());
        Assert.NotNull(services.GetRequiredService<StartupRecoveryService>());
        Assert.True(File.Exists(Path.Combine(_root, "chater.db")));
    }

    [Fact]
    public async Task ChatWorkspaceAndFileTools_AreIsolatedPerWindowScope()
    {
        using var services = new ServiceCollection().AddChaterApplication(new AppPaths(_root)).BuildServiceProvider();
        using var firstScope = services.CreateScope();
        using var secondScope = services.CreateScope();

        var firstWorkspace = firstScope.ServiceProvider.GetRequiredService<ChatWorkspace>();
        var sameWorkspace = firstScope.ServiceProvider.GetRequiredService<ChatWorkspace>();
        var secondWorkspace = secondScope.ServiceProvider.GetRequiredService<ChatWorkspace>();
        var toolNames = (await firstScope.ServiceProvider.GetRequiredService<ChatToolRegistry>().GetTools())
            .Select(tool => tool.Name)
            .ToArray();

        Assert.Same(firstWorkspace, sameWorkspace);
        Assert.NotSame(firstWorkspace, secondWorkspace);
        Assert.Contains("get_workspace_entries", toolNames);
        Assert.Contains("read_workspace_file", toolNames);
        Assert.Contains("write_workspace_file", toolNames);
        Assert.Contains("create_workspace_directory", toolNames);
        Assert.Contains("list_workspace_directory", toolNames);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
