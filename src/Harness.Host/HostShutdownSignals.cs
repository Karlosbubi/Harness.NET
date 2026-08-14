using System.Runtime.InteropServices;

namespace Harness.Host;

internal static class HostShutdownSignals
{
    internal static IDisposable RegisterTermination(CancellationTokenSource shutdown)
    {
        ArgumentNullException.ThrowIfNull(shutdown);
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
            return EmptyRegistration.Instance;

        return PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            shutdown.Cancel();
        });
    }

    private sealed class EmptyRegistration : IDisposable
    {
        internal static EmptyRegistration Instance { get; } = new();
        public void Dispose() { }
    }
}
