using Chater.Services;

namespace Chater.Tests;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Chater.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnsureCreated_CreatesApplicationLogAndExportDirectories()
    {
        var paths = new AppPaths(_root);

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.ApplicationDataDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.True(Directory.Exists(paths.ExportsDirectory));
        Assert.Equal(Path.Combine(_root, "chater.db"), paths.DatabasePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
