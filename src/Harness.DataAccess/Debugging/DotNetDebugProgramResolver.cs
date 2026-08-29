namespace Harness.DataAccess.Debugging;

internal interface IDotNetDebugProgramResolver
{
    string Resolve();
}

internal sealed class DotNetDebugProgramResolver : IDotNetDebugProgramResolver
{
    public string Resolve()
    {
        string executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string? processPath = Environment.ProcessPath;
        if (processPath is not null && Path.GetFileName(processPath)
                .Equals(executableName, StringComparison.OrdinalIgnoreCase))
        {
            return CanonicalExecutable(processPath);
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        foreach (string directory in (path ?? string.Empty).Split(
                     Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try { candidate = Path.GetFullPath(Path.Combine(directory, executableName)); }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            {
                continue;
            }
            if (File.Exists(candidate)) return CanonicalExecutable(candidate);
        }
        throw new DebugAdapterRequestException(
            "A .NET SDK executable is required for debugger launch.");
    }

    private static string CanonicalExecutable(string path)
    {
        FileInfo file = new(Path.GetFullPath(path));
        FileSystemInfo canonical = file.ResolveLinkTarget(returnFinalTarget: true) ?? file;
        if (!File.Exists(canonical.FullName))
            throw new DebugAdapterRequestException(
                "The resolved .NET SDK executable is unavailable.");
        return canonical.FullName;
    }
}
