namespace Harness.BusinessLogic.Inspection;

public enum DotNetProjectKindView
{
    Unknown,
    Library,
    Executable,
    Web,
    Worker,
    Test,
}

public enum DotNetConfigurationSourceView
{
    Convention,
    Declared,
}

public sealed record DotNetConfigurationNameView(string Value);

public sealed record DotNetProjectConfigurationView(
    DotNetConfigurationNameView Name,
    DotNetConfigurationSourceView Source);

public sealed record DotNetProjectDetailsView(
    DotNetProjectKindView Kind,
    IReadOnlyList<DotNetProjectConfigurationView> Configurations,
    bool IsStartupCandidate,
    DotNetLaunchProfileCatalogView? LaunchProfiles = null);
