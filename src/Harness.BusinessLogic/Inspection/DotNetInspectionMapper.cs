using Harness.DataAccess.Inspection;

namespace Harness.BusinessLogic.Inspection;

internal static class DotNetInspectionMapper
{
    internal static WorkspaceDotNetInfoView Map(WorkspaceDotNetInfo result) => new(
        result.EntryPoint,
        result.EntryPointKind,
        result.SdkPolicy is null
            ? null
            : new(
                result.SdkPolicy.Version,
                result.SdkPolicy.RollForward,
                result.SdkPolicy.AllowPrerelease),
        result.Projects.Select(project => new DotNetProjectView(
            project.Path,
            project.Sdk,
            project.TargetFrameworks,
            project.LanguageVersion,
            project.Nullable,
            project.References.Select(reference => new DotNetReferenceView(
                reference.Kind,
                reference.Identity,
                reference.Version)).ToArray(),
            project.Details is null
                ? null
                : new(
                    Map(project.Details.Kind),
                    project.Details.Configurations.Select(configuration =>
                        new DotNetProjectConfigurationView(
                            new(configuration.Name.Value),
                            configuration.Source is DotNetConfigurationSource.Declared
                                ? DotNetConfigurationSourceView.Declared
                                : DotNetConfigurationSourceView.Convention)).ToArray(),
                    project.Details.IsStartupCandidate,
                    project.Details.LaunchProfiles is null
                        ? null
                        : new(
                            project.Details.LaunchProfiles.Profiles.Select(profile =>
                                new DotNetLaunchProfileView(
                                    new(profile.Name.Value),
                                    Map(profile.Kind),
                                    profile.LaunchesBrowser,
                                    profile.HasCommandLineArguments,
                                    profile.EnvironmentNames.Select(name =>
                                        new DotNetLaunchEnvironmentNameView(name.Value)).ToArray()))
                                .ToArray(),
                            project.Details.LaunchProfiles.ErrorCode,
                            project.Details.LaunchProfiles.Error)))).ToArray(),
        result.IsTruncated,
        result.ErrorCode,
        result.Error,
        result.SdkHealth is null
            ? null
            : new(
                result.SdkHealth.State is DotNetSdkHealthState.Ready
                    ? DotNetSdkHealthStateView.Ready
                    : DotNetSdkHealthStateView.Unavailable,
                result.SdkHealth.SelectedVersion is null
                    ? null
                    : new(result.SdkHealth.SelectedVersion.Value),
                result.SdkHealth.WorkloadManifestsAvailable,
                result.SdkHealth.ErrorCode,
                result.SdkHealth.Error),
        result.ProjectIssues?.Select(issue => new DotNetProjectIssueView(
            new(issue.Path.Value),
            Map(issue.Kind),
            issue.Message)).ToArray());

    private static DotNetProjectKindView Map(DotNetProjectKind kind) => kind switch
    {
        DotNetProjectKind.Library => DotNetProjectKindView.Library,
        DotNetProjectKind.Executable => DotNetProjectKindView.Executable,
        DotNetProjectKind.Web => DotNetProjectKindView.Web,
        DotNetProjectKind.Worker => DotNetProjectKindView.Worker,
        DotNetProjectKind.Test => DotNetProjectKindView.Test,
        _ => DotNetProjectKindView.Unknown,
    };

    private static DotNetProjectIssueKindView Map(DotNetProjectIssueKind kind) => kind switch
    {
        DotNetProjectIssueKind.Missing => DotNetProjectIssueKindView.Missing,
        DotNetProjectIssueKind.OutsideWorkspace => DotNetProjectIssueKindView.OutsideWorkspace,
        DotNetProjectIssueKind.TooLarge => DotNetProjectIssueKindView.TooLarge,
        _ => DotNetProjectIssueKindView.InvalidMetadata,
    };

    private static DotNetLaunchProfileKindView Map(DotNetLaunchProfileKind kind) => kind switch
    {
        DotNetLaunchProfileKind.Project => DotNetLaunchProfileKindView.Project,
        DotNetLaunchProfileKind.Executable => DotNetLaunchProfileKindView.Executable,
        _ => DotNetLaunchProfileKindView.Unsupported,
    };
}
