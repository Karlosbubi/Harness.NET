namespace Harness.DataAccess.Debugging;

public sealed record StoredDebugAdapterVersion(string Value);

public sealed record StoredDebugAdapterPlatform(string Value);

public enum StoredDebugAdapterAvailability
{
    Unsupported,
    NotInstalled,
    Installing,
    Ready,
    Corrupt,
}

public sealed record StoredDebugAdapterStatus(
    StoredDebugAdapterAvailability Availability,
    StoredDebugAdapterVersion Version,
    StoredDebugAdapterPlatform Platform,
    string Summary,
    bool CanInstall,
    bool CanRemove);

public interface IDebugAdapterPackageStore
{
    ValueTask<StoredDebugAdapterStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    ValueTask<StoredDebugAdapterStatus> InstallAsync(
        CancellationToken cancellationToken = default);

    ValueTask<StoredDebugAdapterStatus> RemoveAsync(
        CancellationToken cancellationToken = default);
}
