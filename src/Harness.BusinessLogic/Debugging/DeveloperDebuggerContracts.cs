namespace Harness.BusinessLogic.Debugging;

public sealed record DebugAdapterVersion(string Value)
{
    public override string ToString() => Value;
}

public sealed record DebugAdapterPlatform(string Value)
{
    public override string ToString() => Value;
}

public enum DebugAdapterAvailability
{
    Unsupported,
    NotInstalled,
    Installing,
    Ready,
    Corrupt,
}

public sealed record DebugAdapterStatus(
    DebugAdapterAvailability Availability,
    DebugAdapterVersion Version,
    DebugAdapterPlatform Platform,
    string Summary,
    bool CanInstall,
    bool CanRemove);

public interface IDeveloperDebuggerSettingsService
{
    DebugAdapterStatus Current { get; }

    ValueTask<DebugAdapterStatus> GetAsync(CancellationToken cancellationToken = default);

    ValueTask<DebugAdapterStatus> InstallAsync(CancellationToken cancellationToken = default);

    ValueTask<DebugAdapterStatus> RemoveAsync(CancellationToken cancellationToken = default);
}
