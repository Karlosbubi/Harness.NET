namespace Harness.DataAccess.Debugging;

internal interface IDebugAdapterExecutableResolver
{
    ValueTask<string?> ResolveVerifiedExecutableAsync(
        CancellationToken cancellationToken = default);
}
