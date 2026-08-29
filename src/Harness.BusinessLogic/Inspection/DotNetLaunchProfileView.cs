namespace Harness.BusinessLogic.Inspection;

public enum DotNetLaunchProfileKindView
{
    Project,
    Executable,
    Unsupported,
}

public sealed record DotNetLaunchProfileNameView(string Value);

public sealed record DotNetLaunchEnvironmentNameView(string Value);

public sealed record DotNetLaunchProfileView(
    DotNetLaunchProfileNameView Name,
    DotNetLaunchProfileKindView Kind,
    bool LaunchesBrowser,
    bool HasCommandLineArguments,
    IReadOnlyList<DotNetLaunchEnvironmentNameView> EnvironmentNames);

public sealed record DotNetLaunchProfileCatalogView(
    IReadOnlyList<DotNetLaunchProfileView> Profiles,
    string? ErrorCode,
    string? Error);
