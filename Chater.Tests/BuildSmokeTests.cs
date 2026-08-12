using Chater.Services;

namespace Chater.Tests;

public sealed class BuildSmokeTests
{
    [Fact]
    public void ApplicationAssembly_IsLoadable()
    {
        var assembly = typeof(App).Assembly;

        Assert.Equal(AppIdentity.ApplicationName, assembly.GetName().Name);
    }
}
