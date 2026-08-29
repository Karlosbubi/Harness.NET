using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Xml;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.Execution;

internal sealed class DotNetProjectRunner : IDotNetProjectRunner
{
    private const int MaximumOutputCharacters = 256 * 1024;
    private const int MaximumLaunchArguments = 32;
    private const int MaximumLaunchEnvironmentVariables = 32;
    private readonly string executable;
    private readonly string testResultRoot;

    public DotNetProjectRunner(IApplicationPaths applicationPaths) : this(
        "dotnet", Path.Combine(applicationPaths.Current.CacheDirectory, "test-results"))
    {
    }

    internal DotNetProjectRunner(string executable) : this(
        executable, Path.Combine(Path.GetTempPath(), "harness-test-results"))
    {
    }

    internal DotNetProjectRunner(string executable, string testResultRoot)
    {
        this.executable = executable;
        this.testResultRoot = testResultRoot;
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
        if (!TryValidateRunOverrides(
                request.Operation, request.RunOverrides, canonicalRoot,
                out string? workingDirectory, out string? overrideError))
        {
            return Failure(confined, "run_overrides_invalid", overrideError!);
        }

        string? resultDirectory = null;
        if (request.Operation is DotNetProjectOperation.Test)
        {
            resultDirectory = Path.Combine(testResultRoot, Guid.NewGuid().ToString("N"));
        }
        try
        {
            if (resultDirectory is not null) Directory.CreateDirectory(resultDirectory);
            ProcessStartInfo startInfo = new(executable)
            {
                WorkingDirectory = workingDirectory ?? canonicalRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in Arguments(
                         request.Operation, projectPath, request.Test, request.TestScope,
                         request.SelectedTests, resultDirectory, request.RunOverrides,
                         request.TargetFramework, request.Configuration))
            {
                startInfo.ArgumentList.Add(argument);
            }
            if (request.Operation is not DotNetProjectOperation.Run and
                not DotNetProjectOperation.HotReload &&
                request.TargetFramework is { Value: { } target } && target != "unknown")
            {
                startInfo.ArgumentList.Add("--framework");
                startInfo.ArgumentList.Add(target);
            }
            if (request.Operation is not DotNetProjectOperation.Run and
                not DotNetProjectOperation.HotReload &&
                request.Configuration is { Value: { } selectedConfiguration })
            {
                startInfo.ArgumentList.Add("--configuration");
                startInfo.ArgumentList.Add(selectedConfiguration);
            }
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            startInfo.Environment["DOTNET_NOLOGO"] = "1";
            if (request.Operation is DotNetProjectOperation.HotReload)
            {
                startInfo.Environment["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"] = "1";
                startInfo.Environment["DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH"] = "1";
                startInfo.Environment["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";
                startInfo.Environment["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "1";
            }
            if (request.RunOverrides is { Environment.IsDefaultOrEmpty: false } overrides)
            {
                foreach (DotNetLaunchEnvironmentVariable variable in overrides.Environment)
                    startInfo.Environment[variable.Name.Value] = variable.Value.Value;
            }

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
            TrxTestResultParse testResults = new([], false);
            if (resultDirectory is not null)
            {
                try
                {
                    testResults = TrxTestResultParser.ParseDirectory(resultDirectory);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                    or XmlException or InvalidOperationException
                                                    or ArgumentException)
                {
                    testResults = new([], true);
                }
            }
            bool resultsRemoved = TryDeleteResults(resultDirectory);
            string? resultErrorCode = !resultsRemoved
                ? "test_result_cleanup_failed"
                : cancelled ? "cancelled" : null;
            string? resultError = !resultsRemoved
                ? "The private test result files could not be removed."
                : cancelled
                    ? $"The project {request.Operation.ToString().ToLowerInvariant()} was cancelled."
                    : null;
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
                resultErrorCode,
                resultError,
                testResults.Cases,
                testResults.IsTruncated);
        }
        finally
        {
            TryDeleteResults(resultDirectory);
        }
    }

    internal static IReadOnlyList<string> Arguments(
        DotNetProjectOperation operation,
        string projectPath,
        DotNetTestFullyQualifiedName? test,
        DotNetTestScope testScope,
        ImmutableArray<DotNetTestFullyQualifiedName> selectedTests,
        string? resultDirectory,
        DotNetRunOverrides? runOverrides,
        DotNetTargetFramework? targetFramework,
        DotNetConfigurationName? configuration) =>
        operation switch
        {
            DotNetProjectOperation.Build => ["build", projectPath, "--no-restore"],
            DotNetProjectOperation.Rebuild =>
                ["build", projectPath, "--no-restore", "--no-incremental"],
            DotNetProjectOperation.Test when testScope is DotNetTestScope.Exact =>
                ["test", projectPath, "--no-restore", "--filter",
                $"FullyQualifiedName={test!.Value}", "--logger", "trx",
                "--results-directory", resultDirectory!],
            DotNetProjectOperation.Test when testScope is DotNetTestScope.Type =>
                ["test", projectPath, "--no-restore", "--filter",
                $"FullyQualifiedName~{test!.Value}.", "--logger", "trx",
                "--results-directory", resultDirectory!],
            DotNetProjectOperation.Test when testScope is DotNetTestScope.Selection =>
                ["test", projectPath, "--no-restore", "--filter",
                string.Join('|', selectedTests.Select(item =>
                    $"FullyQualifiedName={item.Value}")), "--logger", "trx",
                "--results-directory", resultDirectory!],
            DotNetProjectOperation.Test => ["test", projectPath, "--no-restore",
            "--logger", "trx", "--results-directory", resultDirectory!],
            DotNetProjectOperation.HotReload => WatchArguments(
                projectPath, runOverrides, targetFramework, configuration),
            _ => RunArguments(projectPath, runOverrides, targetFramework, configuration),
        };

    private static IReadOnlyList<string> WatchArguments(
        string projectPath,
        DotNetRunOverrides? overrides,
        DotNetTargetFramework? targetFramework,
        DotNetConfigurationName? configuration)
    {
        List<string> arguments = [
            "watch", "--non-interactive", "--project", projectPath, "run", "--no-restore",
        ];
        AppendTarget(arguments, targetFramework, configuration);
        AppendLaunchArguments(arguments, overrides);
        return arguments;
    }

    private static IReadOnlyList<string> RunArguments(
        string projectPath,
        DotNetRunOverrides? overrides,
        DotNetTargetFramework? targetFramework,
        DotNetConfigurationName? configuration)
    {
        List<string> arguments = ["run", "--project", projectPath, "--no-restore"];
        AppendTarget(arguments, targetFramework, configuration);
        AppendLaunchArguments(arguments, overrides);
        return arguments;
    }

    private static void AppendTarget(
        List<string> arguments,
        DotNetTargetFramework? targetFramework,
        DotNetConfigurationName? configuration)
    {
        if (targetFramework is { Value: { } target } && target != "unknown")
        {
            arguments.Add("--framework");
            arguments.Add(target);
        }
        if (configuration is { Value: { } selected })
        {
            arguments.Add("--configuration");
            arguments.Add(selected);
        }
    }

    private static void AppendLaunchArguments(
        List<string> arguments,
        DotNetRunOverrides? overrides)
    {
        if (overrides?.LaunchProfile is { } profile)
        {
            arguments.Add("--launch-profile");
            arguments.Add(profile.Value);
        }
        else
        {
            arguments.Add("--no-launch-profile");
        }
        if (overrides is { Arguments.IsDefaultOrEmpty: false })
        {
            arguments.Add("--");
            arguments.AddRange(overrides.Arguments.Select(argument => argument.Value));
        }
    }

    internal static bool TryValidateRunOverrides(
        DotNetProjectOperation operation,
        DotNetRunOverrides? overrides,
        string root,
        out string? workingDirectory,
        out string? error)
    {
        workingDirectory = null;
        error = null;
        if (overrides is null) return true;
        if (operation is not DotNetProjectOperation.Run and not DotNetProjectOperation.HotReload)
        {
            error = "One-run overrides are valid only for Run.";
            return false;
        }
        if (overrides.LaunchProfile is { Value: { } profile } &&
            (!IsBoundedText(profile, 128) || profile.Any(char.IsControl)))
        {
            error = "The launch profile name is invalid.";
            return false;
        }
        if (overrides.Arguments.IsDefault ||
            overrides.Arguments.Length > MaximumLaunchArguments ||
            overrides.Arguments.Any(argument =>
                !IsBoundedText(argument.Value, 1_024) || argument.Value.Any(char.IsControl)) ||
            overrides.Arguments.Sum(argument => argument.Value.Length) > 8_192)
        {
            error = $"Run accepts at most {MaximumLaunchArguments} bounded arguments.";
            return false;
        }
        if (overrides.Environment.IsDefault ||
            overrides.Environment.Length > MaximumLaunchEnvironmentVariables ||
            overrides.Environment.Select(variable => variable.Name.Value)
                .Distinct(StringComparer.Ordinal).Count() != overrides.Environment.Length ||
            overrides.Environment.Any(variable =>
                !IsEnvironmentName(variable.Name.Value) ||
                variable.Value.Value.Length > 4_096 ||
                variable.Value.Value.Contains('\0')) ||
            overrides.Environment.Sum(variable => variable.Value.Value.Length) > 16_384)
        {
            error = $"Run accepts at most {MaximumLaunchEnvironmentVariables} bounded environment overrides.";
            return false;
        }
        if (overrides.WorkingDirectory is null) return true;
        if (!WorkspacePathPolicy.TryResolve(
                root, overrides.WorkingDirectory.Value, out _, out _,
                out string absolute, out _, out _) || !Directory.Exists(absolute))
        {
            error = "The one-run working directory must be an existing workspace directory.";
            return false;
        }
        workingDirectory = absolute;
        return true;
    }

    private static bool IsBoundedText(string value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum &&
        value.Equals(value.Trim(), StringComparison.Ordinal);

    private static bool IsEnvironmentName(string value) =>
        IsBoundedText(value, 128) &&
        (char.IsLetter(value[0]) || value[0] == '_') &&
        value.All(character => char.IsLetterOrDigit(character) || character == '_') &&
        !value.Equals("DOTNET_CLI_TELEMETRY_OPTOUT", StringComparison.Ordinal) &&
        !value.Equals("DOTNET_NOLOGO", StringComparison.Ordinal);

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

    private static bool TryDeleteResults(string? directory)
    {
        if (directory is null || !Directory.Exists(directory)) return true;
        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record BoundedText(string Value, bool IsTruncated);
}
