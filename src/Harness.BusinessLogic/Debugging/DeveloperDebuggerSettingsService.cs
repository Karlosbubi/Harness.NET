using Harness.DataAccess.Debugging;
using Microsoft.Extensions.Logging;

namespace Harness.BusinessLogic.Debugging;

internal sealed class DeveloperDebuggerSettingsService(
    IDebugAdapterPackageStore packageStore,
    ILogger<DeveloperDebuggerSettingsService> logger) : IDeveloperDebuggerSettingsService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private DebugAdapterStatus current = new(
        DebugAdapterAvailability.NotInstalled,
        new("3.2.0-1092"),
        new("unknown"),
        "Debugger status has not been verified in this process.",
        CanInstall: true,
        CanRemove: false);

    public DebugAdapterStatus Current => Volatile.Read(ref current);

    public async ValueTask<DebugAdapterStatus> GetAsync(CancellationToken cancellationToken = default)
    {
        DebugAdapterStatus status = Map(await packageStore.GetStatusAsync(cancellationToken));
        Volatile.Write(ref current, status);
        return status;
    }

    public async ValueTask<DebugAdapterStatus> InstallAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            DebugAdapterStatus result = Map(await packageStore.InstallAsync(cancellationToken));
            Volatile.Write(ref current, result);
            logger.LogInformation(
                "Managed debug adapter {Version} is {Availability} for {Platform}",
                result.Version.Value,
                result.Availability,
                result.Platform.Value);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<DebugAdapterStatus> RemoveAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            DebugAdapterStatus result = Map(await packageStore.RemoveAsync(cancellationToken));
            Volatile.Write(ref current, result);
            logger.LogInformation(
                "Managed debug adapter {Version} was removed for {Platform}",
                result.Version.Value,
                result.Platform.Value);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private static DebugAdapterStatus Map(StoredDebugAdapterStatus status) =>
        new(status.Availability switch
        {
            StoredDebugAdapterAvailability.Unsupported => DebugAdapterAvailability.Unsupported,
            StoredDebugAdapterAvailability.NotInstalled => DebugAdapterAvailability.NotInstalled,
            StoredDebugAdapterAvailability.Installing => DebugAdapterAvailability.Installing,
            StoredDebugAdapterAvailability.Ready => DebugAdapterAvailability.Ready,
            StoredDebugAdapterAvailability.Corrupt => DebugAdapterAvailability.Corrupt,
            _ => throw new InvalidOperationException("Unsupported debug adapter status."),
        }, new(status.Version.Value), new(status.Platform.Value), status.Summary,
            status.CanInstall, status.CanRemove);
}
