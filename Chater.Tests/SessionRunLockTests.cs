using Chater.Services;

namespace Chater.Tests;

public sealed class SessionRunLockTests
{
    [Fact]
    public async Task AcquireAsync_SerializesOperationsForTheSameConversation()
    {
        var runLock = new SessionRunLock();
        var firstLease = await runLock.AcquireAsync("conversation");
        var secondLeaseTask = runLock.AcquireAsync("conversation");

        await Task.Delay(20);
        Assert.False(secondLeaseTask.IsCompleted);

        firstLease.Dispose();
        using var secondLease = await secondLeaseTask.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
