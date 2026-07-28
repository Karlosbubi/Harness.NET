namespace Harness.Host;

internal static class HostRunModeResolver
{
    internal const string NoUiArgument = "--no-ui";
    internal const string WaitForShutdownArgument = "--wait-for-shutdown";
    internal const string BackupPathPrefix = "--backup-path=";
    internal const string UiPrefix = "--ui=";

    internal static HostRunMode Resolve(
        IReadOnlyCollection<string> args,
        bool isInputRedirected,
        bool isOutputRedirected)
    {
        ValidateArguments(args);
        return args.Contains(WaitForShutdownArgument, StringComparer.Ordinal)
            ? HostRunMode.WaitForShutdown
            : args.Contains(NoUiArgument, StringComparer.Ordinal)
                ? HostRunMode.Initialize
                : HostRunMode.Interactive;
    }

    internal static InteractiveFrontend ResolveFrontend(
        IReadOnlyCollection<string> args,
        bool isInputRedirected,
        bool isOutputRedirected)
    {
        ValidateArguments(args);
        string? value = args.SingleOrDefault(argument =>
            argument.StartsWith(UiPrefix, StringComparison.Ordinal));
        InteractiveFrontend frontend = value switch
        {
            null or "--ui=avalonia" => InteractiveFrontend.Avalonia,
            "--ui=terminal" => InteractiveFrontend.Terminal,
            _ => throw new ArgumentException($"Unsupported UI selection '{value}'."),
        };
        if (frontend is InteractiveFrontend.Terminal &&
            (isInputRedirected || isOutputRedirected))
        {
            throw new ArgumentException("Terminal UI requires attached input and output streams.");
        }

        return frontend;
    }

    internal static bool IsOperationalArgument(string argument) =>
        argument.Equals(NoUiArgument, StringComparison.Ordinal) ||
        argument.Equals(WaitForShutdownArgument, StringComparison.Ordinal) ||
        argument.StartsWith(BackupPathPrefix, StringComparison.Ordinal) ||
        argument.StartsWith(UiPrefix, StringComparison.Ordinal);

    internal static string? BackupPath(IReadOnlyCollection<string> args) => args
        .SingleOrDefault(argument =>
            argument.StartsWith(BackupPathPrefix, StringComparison.Ordinal))?
        [BackupPathPrefix.Length..];

    private static void ValidateArguments(IReadOnlyCollection<string> args)
    {
        string[] uiArguments = args
            .Where(argument => argument.StartsWith(UiPrefix, StringComparison.Ordinal))
            .ToArray();
        if (uiArguments.Length > 1)
        {
            throw new ArgumentException("Specify at most one --ui option.");
        }

        bool hasNonUiMode = args.Any(argument =>
            argument.Equals(NoUiArgument, StringComparison.Ordinal) ||
            argument.Equals(WaitForShutdownArgument, StringComparison.Ordinal) ||
            argument.StartsWith(BackupPathPrefix, StringComparison.Ordinal));
        if (hasNonUiMode && uiArguments.Length > 0)
        {
            throw new ArgumentException("--ui cannot be combined with an operational mode.");
        }
    }
}
