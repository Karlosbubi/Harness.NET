using Microsoft.Build.Locator;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed class MSBuildRuntime(DotNetSdkSelector sdkSelector) : IMSBuildRuntime
{
    private static readonly SemaphoreSlim RegistrationGate = new(1, 1);
    private static DotNetSdkSelection? registered;

    public async ValueTask<MSBuildRuntimeResult> EnsureRegisteredAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        DotNetSdkSelection selected = await sdkSelector.SelectAsync(
            workspaceRoot,
            cancellationToken);
        if (selected.Version is null || selected.Path is null)
        {
            return new(
                MSBuildRuntimeState.Degraded,
                null,
                null,
                selected.ErrorCode,
                selected.Error);
        }

        await RegistrationGate.WaitAsync(cancellationToken);
        try
        {
            if (registered is { Version: { } version, Path: { } path })
            {
                return path == selected.Path
                    ? Ready(version, path)
                    : new(
                        MSBuildRuntimeState.Degraded,
                        selected.Version,
                        selected.Path,
                        "sdk_change_requires_restart",
                        $"Code intelligence is registered with SDK {version.Value}; " +
                        $"workspace SDK {selected.Version.Value} requires an application restart.");
            }

            try
            {
                MSBuildLocator.RegisterMSBuildPath(selected.Path.Value);
                registered = selected;
                return Ready(selected.Version, selected.Path);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or ArgumentException)
            {
                return new(
                    MSBuildRuntimeState.Failed,
                    selected.Version,
                    selected.Path,
                    "msbuild_registration_failed",
                    exception.Message);
            }
        }
        finally
        {
            RegistrationGate.Release();
        }
    }

    private static MSBuildRuntimeResult Ready(
        DotNetSdkVersion version,
        DotNetSdkPath path) =>
        new(MSBuildRuntimeState.Ready, version, path, ErrorCode: null, Error: null);
}
