using System.ComponentModel;
using System.Diagnostics;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed class DotNetSdkSelector(IDotNetProcess dotNetProcess)
{
    internal async ValueTask<DotNetSdkSelection> SelectAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        string root;
        try
        {
            root = Path.GetFullPath(workspaceRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return DotNetSdkSelection.Degraded("invalid_workspace_root", exception.Message);
        }

        if (!Directory.Exists(root))
        {
            return DotNetSdkSelection.Degraded(
                "workspace_missing",
                "The workspace root does not exist.");
        }

        DotNetProcessResult selected = await dotNetProcess.RunAsync(
            root,
            "--version",
            cancellationToken);
        if (selected.ExitCode != 0 || string.IsNullOrWhiteSpace(selected.StandardOutput))
        {
            return DotNetSdkSelection.Degraded(
                "sdk_unavailable",
                FirstUsefulLine(selected.StandardError) ??
                "The workspace's global.json does not resolve to an installed .NET SDK.");
        }

        string version = selected.StandardOutput.Trim().Split('\n')[0].Trim();
        DotNetProcessResult installed = await dotNetProcess.RunAsync(
            root,
            "--list-sdks",
            cancellationToken);
        if (installed.ExitCode != 0)
        {
            return DotNetSdkSelection.Degraded(
                "sdk_inventory_unavailable",
                FirstUsefulLine(installed.StandardError) ?? "Installed SDKs could not be listed.");
        }

        string? basePath = installed.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith($"{version} [", StringComparison.Ordinal))
            .Select(ParseBasePath)
            .FirstOrDefault(path => path is not null);
        if (basePath is null)
        {
            return DotNetSdkSelection.Degraded(
                "sdk_path_missing",
                $"The selected SDK {version} was not present in the installed SDK inventory.");
        }

        string sdkPath = Path.GetFullPath(Path.Combine(basePath, version));
        if (!File.Exists(Path.Combine(sdkPath, "MSBuild.dll")))
        {
            return DotNetSdkSelection.Degraded(
                "msbuild_missing",
                $"The selected SDK {version} does not contain MSBuild.dll.");
        }

        return new(new(version), new(sdkPath), ErrorCode: null, Error: null);
    }

    private static string? ParseBasePath(string line)
    {
        int open = line.LastIndexOf('[');
        int close = line.LastIndexOf(']');
        return open >= 0 && close > open + 1
            ? line[(open + 1)..close].Trim()
            : null;
    }

    private static string? FirstUsefulLine(string value)
    {
        const int maximumLength = 2_048;
        string? line = value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return line is null || line.Length <= maximumLength
            ? line
            : line[..maximumLength];
    }
}

internal sealed record DotNetSdkSelection(
    DotNetSdkVersion? Version,
    DotNetSdkPath? Path,
    string? ErrorCode,
    string? Error)
{
    internal static DotNetSdkSelection Degraded(string code, string error) =>
        new(null, null, code, error);
}

internal interface IDotNetProcess
{
    ValueTask<DotNetProcessResult> RunAsync(
        string workingDirectory,
        string argument,
        CancellationToken cancellationToken);
}

internal sealed record DotNetProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal sealed class DotNetProcess : IDotNetProcess
{
    public async ValueTask<DotNetProcessResult> RunAsync(
        string workingDirectory,
        string argument,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        try
        {
            using Process process = Process.Start(startInfo) ??
                throw new InvalidOperationException("The dotnet host did not start.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new(process.ExitCode, await output, await error);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException)
        {
            return new(-1, string.Empty, exception.Message);
        }
    }
}
