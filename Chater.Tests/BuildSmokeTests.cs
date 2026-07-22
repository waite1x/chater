namespace Chater.Tests;

public sealed class BuildSmokeTests
{
    [Fact]
    public void ApplicationAssembly_IsLoadable()
    {
        var assembly = typeof(App).Assembly;

        Assert.Equal("Chater", assembly.GetName().Name);
    }
}
