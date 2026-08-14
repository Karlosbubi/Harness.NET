namespace Harness.Host.Tests;

public sealed class HostShutdownSignalsTests
{
    [Fact]
    public void Termination_registration_is_bounded_and_disposable()
    {
        using CancellationTokenSource shutdown = new();

        using IDisposable registration = HostShutdownSignals.RegisterTermination(shutdown);

        Assert.False(shutdown.IsCancellationRequested);
    }
}
