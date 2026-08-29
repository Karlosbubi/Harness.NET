using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.Execution;

internal sealed class DotNetProjectRunner : IDotNetProjectRunner
{
    private const int MaximumOutputCharacters = 256 * 1024;
    private readonly string executable;

    public DotNetProjectRunner() : this("dotnet")
    {
    }

    internal DotNetProjectRunner(string executable)
    {
        this.executable = executable;
    }

    public async ValueTask<DotNetProjectExecutionResult> RunAsync(
        string sourceRoot,
        DotNetProjectExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProjectPath is null || string.IsNullOrWhiteSpace(request.ProjectPath.Value))
        {
            return Failure(request, "invalid_project_path",
                "A confined project path is required.");
        }
        if (!Enum.IsDefined(request.Operation))
        {
            return Failure(request, "operation_invalid",
                "The selected project operation is invalid.");
        }
        if (!Enum.IsDefined(request.TestScope))
        {
            return Failure(request, "test_scope_invalid",
                "The selected test scope is invalid.");
        }
        if (!WorkspacePathPolicy.TryResolve(
                sourceRoot,
                request.ProjectPath.Value,
                out string canonicalRoot,
                out string confinedProject,
                out string projectPath,
                out string? errorCode,
                out string? error))
        {
            return Failure(request, errorCode!, error!);
        }
        DotNetProjectExecutionRequest confined = request with
        {
            ProjectPath = new(confinedProject.Replace(Path.DirectorySeparatorChar, '/')),
        };
        FileInfo project = new(projectPath);
        if (!project.Exists || project.LinkTarget is not null ||
            !project.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(confined, "project_unavailable",
                "The selected C# project is missing, symbolic, or unsupported.");
        }
        if (request.TargetFramework is { Value: { } framework } &&
            !IsValidFramework(framework))
        {
            return Failure(confined, "target_framework_invalid",
                "The selected target framework is invalid.");
        }
        if (request.Configuration is { Value: { } configuration } &&
            !IsValidConfiguration(configuration))
        {
            return Failure(confined, "configuration_invalid",
                "The selected build configuration is invalid.");
        }
        if (request.Operation is DotNetProjectOperation.Test)
        {
            bool valid = request.Test is not null && request.TestScope switch
            {
                DotNetTestScope.Exact or DotNetTestScope.Type =>
                    IsValidTestName(request.Test.Value),
                DotNetTestScope.Project => request.Test.Value.Equals(
                    confined.ProjectPath.Value, StringComparison.Ordinal),
                DotNetTestScope.Selection => IsValidSelection(request.SelectedTests),
                _ => false,
            };
            if (!valid || request.TestScope is not DotNetTestScope.Selection &&
                !request.SelectedTests.IsDefaultOrEmpty)
            {
                return Failure(confined, "test_name_invalid",
                    "A bounded fully qualified test name is required.");
            }
        }
        else if (request.Test is not null || request.TestScope is not DotNetTestScope.Exact)
        {
            return Failure(confined, "test_target_invalid",
                "A test selector is valid only for the Test operation.");
        }

        ProcessStartInfo startInfo = new(executable)
        {
            WorkingDirectory = canonicalRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in Arguments(
                     request.Operation, projectPath, request.Test, request.TestScope,
                     request.SelectedTests))
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (request.TargetFramework is { Value: { } target } && target != "unknown")
        {
            startInfo.ArgumentList.Add("--framework");
            startInfo.ArgumentList.Add(target);
        }
        if (request.Configuration is { Value: { } selectedConfiguration })
        {
            startInfo.ArgumentList.Add("--configuration");
            startInfo.ArgumentList.Add(selectedConfiguration);
        }
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using Process process = new() { StartInfo = startInfo };
        Stopwatch duration = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                return Failure(confined, "process_start_failed",
                    "The dotnet process did not start.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return Failure(confined, "process_start_failed", exception.Message);
        }

        Task<BoundedText> output = ReadBoundedAsync(process.StandardOutput);
        Task<BoundedText> diagnostic = ReadBoundedAsync(process.StandardError);
        bool cancelled = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(CancellationToken.None);
        }

        BoundedText standardOutput = await output;
        BoundedText standardError = await diagnostic;
        duration.Stop();
        return new(
            confined.ProjectPath,
            confined.TargetFramework,
            process.ExitCode,
            new(standardOutput.Value),
            new(standardError.Value),
            standardOutput.IsTruncated,
            standardError.IsTruncated,
            cancelled,
            duration.ElapsedMilliseconds,
            cancelled ? "cancelled" : null,
            cancelled ? $"The project {request.Operation.ToString().ToLowerInvariant()} was cancelled." : null);
    }

    private static IReadOnlyList<string> Arguments(
        DotNetProjectOperation operation,
        string projectPath,
        DotNetTestFullyQualifiedName? test,
        DotNetTestScope testScope,
        ImmutableArray<DotNetTestFullyQualifiedName> selectedTests) =>
        operation switch
    {
        DotNetProjectOperation.Build => ["build", projectPath, "--no-restore"],
        DotNetProjectOperation.Rebuild =>
            ["build", projectPath, "--no-restore", "--no-incremental"],
        DotNetProjectOperation.Test when testScope is DotNetTestScope.Exact =>
            ["test", projectPath, "--no-restore", "--filter",
                $"FullyQualifiedName={test!.Value}"],
        DotNetProjectOperation.Test when testScope is DotNetTestScope.Type =>
            ["test", projectPath, "--no-restore", "--filter",
                $"FullyQualifiedName~{test!.Value}."],
        DotNetProjectOperation.Test when testScope is DotNetTestScope.Selection =>
            ["test", projectPath, "--no-restore", "--filter",
                string.Join('|', selectedTests.Select(item =>
                    $"FullyQualifiedName={item.Value}"))],
        DotNetProjectOperation.Test => ["test", projectPath, "--no-restore"],
        _ => ["run", "--project", projectPath, "--no-restore", "--no-launch-profile"],
    };

    private static bool IsValidFramework(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.Equals(value.Trim(), StringComparison.Ordinal) &&
        value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-');

    private static bool IsValidConfiguration(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.Equals(value.Trim(), StringComparison.Ordinal) &&
        value.All(character => !char.IsControl(character));

    private static bool IsValidTestName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 512 &&
        value.Equals(value.Trim(), StringComparison.Ordinal) &&
        value.All(character => char.IsLetterOrDigit(character) ||
            character is '.' or '_' or '+' or '`');

    private static bool IsValidSelection(
        ImmutableArray<DotNetTestFullyQualifiedName> tests) =>
        !tests.IsDefault && tests.Length is >= 2 and <= 24 &&
        tests.Select(test => test.Value).Distinct(StringComparer.Ordinal).Count() == tests.Length &&
        tests.All(test => IsValidTestName(test.Value)) &&
        tests.Sum(test => test.Value.Length) <= 12_000;

    private static async Task<BoundedText> ReadBoundedAsync(StreamReader reader)
    {
        char[] buffer = new char[4096];
        StringBuilder kept = new(MaximumOutputCharacters);
        bool truncated = false;
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (read == 0)
            {
                break;
            }
            int remaining = MaximumOutputCharacters - kept.Length;
            if (remaining > 0)
            {
                kept.Append(buffer, 0, Math.Min(read, remaining));
            }
            truncated |= read > remaining;
        }
        return new(kept.ToString().TrimEnd(), truncated);
    }

    private static DotNetProjectExecutionResult Failure(
        DotNetProjectExecutionRequest request,
        string code,
        string error) => new(
        request.ProjectPath ?? new(string.Empty),
        request.TargetFramework,
        ExitCode: null,
        new(string.Empty),
        new(string.Empty),
        IsOutputTruncated: false,
        IsErrorTruncated: false,
        WasCancelled: false,
        DurationMilliseconds: 0,
        code,
        error);

    private sealed record BoundedText(string Value, bool IsTruncated);
}
