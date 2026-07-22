using Chater.Composition;
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
