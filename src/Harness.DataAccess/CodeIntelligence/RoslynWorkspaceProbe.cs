using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed class RoslynWorkspaceProbe(IMSBuildRuntime msBuildRuntime)
    : IRoslynWorkspaceProbe
{
    public async ValueTask<RoslynWorkspaceProbeResult> ProbeAsync(
        string workspaceRoot,
        string entryPoint,
        CancellationToken cancellationToken = default)
    {
        MSBuildRuntimeResult runtime = await msBuildRuntime.EnsureRegisteredAsync(
            workspaceRoot,
            cancellationToken);
        if (runtime.State is not MSBuildRuntimeState.Ready)
        {
            return new(
                runtime.State is MSBuildRuntimeState.Failed
                    ? RoslynWorkspaceProbeState.Failed
                    : RoslynWorkspaceProbeState.Degraded,
                runtime.SdkVersion,
                0,
                0,
                [new(runtime.ErrorCode ?? "sdk_unavailable", runtime.Error ?? "SDK unavailable.")]);
        }

        string root = Path.GetFullPath(workspaceRoot);
        string path = Path.IsPathRooted(entryPoint)
            ? Path.GetFullPath(entryPoint)
            : Path.GetFullPath(entryPoint, root);
        string relative = Path.GetRelativePath(root, path);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) || !File.Exists(path))
        {
            return new(
                RoslynWorkspaceProbeState.Failed,
                runtime.SdkVersion,
                0,
                0,
                [new("invalid_entry_point", "The code-intelligence entry point is outside the workspace or missing.")]);
        }

        return await ProbeRegisteredAsync(path, runtime.SdkVersion!, cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async ValueTask<RoslynWorkspaceProbeResult> ProbeRegisteredAsync(
        string entryPoint,
        DotNetSdkVersion sdkVersion,
        CancellationToken cancellationToken)
    {
        ConcurrentQueue<RoslynWorkspaceProbeIssue> issues = new();
        Dictionary<string, string> properties = new(StringComparer.Ordinal)
        {
            ["DesignTimeBuild"] = "true",
            ["BuildingInsideVisualStudio"] = "true",
            ["SkipCompilerExecution"] = "true",
        };
        using MSBuildWorkspace workspace = MSBuildWorkspace.Create(properties);
        using IDisposable workspaceFailure = workspace.RegisterWorkspaceFailedHandler(args =>
            EnqueueIssue(
                issues,
                args.Diagnostic.Kind.ToString().ToLowerInvariant(),
                args.Diagnostic.Message));
        try
        {
            string extension = Path.GetExtension(entryPoint).ToLowerInvariant();
            ImmutableArray<Project> projects = extension switch
            {
                ".sln" or ".slnx" =>
                    (await workspace.OpenSolutionAsync(entryPoint, cancellationToken: cancellationToken))
                    .Projects.ToImmutableArray(),
                ".csproj" or ".fsproj" or ".vbproj" =>
                    [(await workspace.OpenProjectAsync(entryPoint, cancellationToken: cancellationToken))],
                _ => [],
            };
            if (projects.Length == 0)
            {
                EnqueueIssue(
                    issues,
                    "unsupported_or_empty_entry_point",
                    "The entry point did not load any projects.");
            }

            RoslynWorkspaceProbeState state = projects.Length == 0
                ? RoslynWorkspaceProbeState.Failed
                : issues.Count == 0
                    ? RoslynWorkspaceProbeState.Ready
                    : RoslynWorkspaceProbeState.Degraded;
            return new(
                state,
                sdkVersion,
                projects.Length,
                projects.Sum(project => project.DocumentIds.Count),
                issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or ArgumentException)
        {
            EnqueueIssue(issues, "workspace_load_failed", exception.Message);
            return new(RoslynWorkspaceProbeState.Failed, sdkVersion, 0, 0, issues.ToArray());
        }
    }

    private static void EnqueueIssue(
        ConcurrentQueue<RoslynWorkspaceProbeIssue> issues,
        string code,
        string message)
    {
        const int maximumIssueCount = 100;
        const int maximumMessageLength = 2_048;
        if (issues.Count >= maximumIssueCount)
        {
            return;
        }

        string boundedMessage = message.Length <= maximumMessageLength
            ? message
            : message[..maximumMessageLength];
        issues.Enqueue(new(code, boundedMessage));
    }
}
