namespace Harness.DataAccess.Inspection;

public enum DotNetProjectKind
{
    Unknown,
    Library,
    Executable,
    Web,
    Worker,
    Test,
}

public enum DotNetConfigurationSource
{
    Convention,
    Declared,
}

public sealed record DotNetConfigurationName(string Value);

public sealed record DotNetProjectConfiguration(
    DotNetConfigurationName Name,
    DotNetConfigurationSource Source);

public sealed record DotNetProjectDetails(
    DotNetProjectKind Kind,
    IReadOnlyList<DotNetProjectConfiguration> Configurations,
    bool IsStartupCandidate);
