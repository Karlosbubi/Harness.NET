using System.Collections.Immutable;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.Debugging;

internal sealed class DotNetDebugSessionFactory(
    IDebugAdapterSessionFactory adapterFactory) : IDotNetDebugSessionFactory
{
    public async ValueTask<IDebugAdapterSession> StartLaunchAsync(
        string sourceRoot,
        StoredDotNetDebugLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!WorkspacePathPolicy.TryResolve(
                sourceRoot,
                request.ProjectPath.Value,
                out string canonicalRoot,
                out _,
                out string projectPath,
                out _,
                out string? error))
        {
            throw new DebugAdapterRequestException(error ??
                "The debug project path is outside the source context.");
        }
        FileInfo project = new(projectPath);
        if (!project.Exists || project.LinkTarget is not null ||
            !project.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new DebugAdapterRequestException(
                "The debug project is missing, symbolic, or unsupported.");
        }
        if (!DotNetProjectRunner.TryValidateRunOverrides(
                DotNetProjectOperation.Run,
                request.RunOverrides,
                canonicalRoot,
                out string? workingDirectory,
                out string? overrideError))
        {
            throw new DebugAdapterRequestException(overrideError ??
                "The debug launch overrides are invalid.");
        }
        IReadOnlyList<string> arguments = DotNetProjectRunner.Arguments(
            DotNetProjectOperation.Run,
            projectPath,
            test: null,
            DotNetTestScope.Exact,
            [],
            resultDirectory: null,
            request.RunOverrides,
            request.TargetFramework,
            request.Configuration);
        List<StoredDebugEnvironmentEntry> environment =
        [
            new(new("DOTNET_CLI_TELEMETRY_OPTOUT"), new("1")),
            new(new("DOTNET_NOLOGO"), new("1")),
        ];
        if (request.RunOverrides is { Environment.IsDefaultOrEmpty: false } overrides)
        {
            environment.AddRange(overrides.Environment.Select(variable =>
                new StoredDebugEnvironmentEntry(
                    new(variable.Name.Value), new(variable.Value.Value))));
        }
        return await adapterFactory.StartAsync(new(
            request.SessionId,
            StoredDebugAdapterStartKind.Launch,
            new(canonicalRoot),
            new(workingDirectory ?? canonicalRoot),
            arguments.Select(argument => new StoredDebugArgument(argument)).ToImmutableArray(),
            environment.ToImmutableArray(),
            OwnedProcessId: null,
            request.StopAtEntry,
            request.JustMyCode), cancellationToken);
    }
}
