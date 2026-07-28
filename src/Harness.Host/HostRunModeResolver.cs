namespace Harness.Host;

internal static class HostRunModeResolver
{
    internal const string NoUiArgument = "--no-ui";
    internal const string WaitForShutdownArgument = "--wait-for-shutdown";
    internal const string BackupPathPrefix = "--backup-path=";

    internal static HostRunMode Resolve(
        IReadOnlyCollection<string> args,
        bool isInputRedirected,
        bool isOutputRedirected) =>
        args.Contains(WaitForShutdownArgument, StringComparer.Ordinal)
            ? HostRunMode.WaitForShutdown
            : args.Contains(NoUiArgument, StringComparer.Ordinal) ||
              isInputRedirected ||
              isOutputRedirected
                ? HostRunMode.Initialize
                : HostRunMode.Interactive;

    internal static bool IsOperationalArgument(string argument) =>
        argument.Equals(NoUiArgument, StringComparison.Ordinal) ||
        argument.Equals(WaitForShutdownArgument, StringComparison.Ordinal) ||
        argument.StartsWith(BackupPathPrefix, StringComparison.Ordinal);

    internal static string? BackupPath(IReadOnlyCollection<string> args) => args
        .SingleOrDefault(argument =>
            argument.StartsWith(BackupPathPrefix, StringComparison.Ordinal))?
        [BackupPathPrefix.Length..];
}
