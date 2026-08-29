using Harness.BusinessLogic.Debugging;
using Harness.DataAccess.Debugging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.BusinessLogic.Tests.Debugging;

public sealed class DeveloperDebuggerSettingsServiceTests
{
    [Fact]
    public async Task Maps_managed_package_lifecycle_without_exposing_an_executable_path()
    {
        PackageStore store = new();
        DeveloperDebuggerSettingsService service = new(
            store, NullLogger<DeveloperDebuggerSettingsService>.Instance);

        DebugAdapterStatus initial = await service.GetAsync();
        DebugAdapterStatus installed = await service.InstallAsync();
        DebugAdapterStatus removed = await service.RemoveAsync();

        Assert.Equal(DebugAdapterAvailability.NotInstalled, initial.Availability);
        Assert.Equal(DebugAdapterAvailability.Ready, installed.Availability);
        Assert.Equal(new DebugAdapterVersion("3.2.0-1092"), installed.Version);
        Assert.Equal(new DebugAdapterPlatform("linux-x64"), installed.Platform);
        Assert.Equal(DebugAdapterAvailability.NotInstalled, removed.Availability);
        Assert.Equal(1, store.InstallCalls);
        Assert.Equal(1, store.RemoveCalls);
    }

    private sealed class PackageStore : IDebugAdapterPackageStore
    {
        private StoredDebugAdapterAvailability availability =
            StoredDebugAdapterAvailability.NotInstalled;

        internal int InstallCalls { get; private set; }
        internal int RemoveCalls { get; private set; }

        public ValueTask<StoredDebugAdapterStatus> GetStatusAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Status());

        public ValueTask<StoredDebugAdapterStatus> InstallAsync(
            CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            availability = StoredDebugAdapterAvailability.Ready;
            return ValueTask.FromResult(Status());
        }

        public ValueTask<StoredDebugAdapterStatus> RemoveAsync(
            CancellationToken cancellationToken = default)
        {
            RemoveCalls++;
            availability = StoredDebugAdapterAvailability.NotInstalled;
            return ValueTask.FromResult(Status());
        }

        private StoredDebugAdapterStatus Status() => new(
            availability,
            new("3.2.0-1092"),
            new("linux-x64"),
            availability is StoredDebugAdapterAvailability.Ready ? "Ready." : "Absent.",
            CanInstall: availability is not StoredDebugAdapterAvailability.Ready,
            CanRemove: availability is StoredDebugAdapterAvailability.Ready);
    }
}
