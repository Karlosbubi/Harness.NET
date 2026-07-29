namespace Harness.DataAccess.Configuration;

public sealed record ApplicationPaths(
    string ConfigDirectory,
    string DataDirectory,
    string StateDirectory,
    string CacheDirectory,
    string DatabasePath,
    string LogDirectory,
    string WorktreeDirectory)
{
    public string WorkbenchLayoutPath =>
        Path.Combine(StateDirectory, "workbench-layout.json");
}
