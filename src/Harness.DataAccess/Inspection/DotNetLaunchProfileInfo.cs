namespace Harness.DataAccess.Inspection;

public enum DotNetLaunchProfileKind
{
    Project,
    Executable,
    Unsupported,
}

public sealed record DotNetLaunchProfileName(string Value);

public sealed record DotNetLaunchEnvironmentName(string Value);

public sealed record DotNetLaunchProfileInfo(
    DotNetLaunchProfileName Name,
    DotNetLaunchProfileKind Kind,
    bool LaunchesBrowser,
    bool HasCommandLineArguments,
    IReadOnlyList<DotNetLaunchEnvironmentName> EnvironmentNames);

public sealed record DotNetLaunchProfileCatalog(
    IReadOnlyList<DotNetLaunchProfileInfo> Profiles,
    string? ErrorCode,
    string? Error);
