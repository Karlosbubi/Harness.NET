namespace Harness.DataAccess.Configuration;

internal sealed class XdgApplicationPaths : IApplicationPaths
{
    private const string ApplicationDirectoryName = "harness.net";
    private readonly Func<string, string?> getEnvironmentVariable;
    private readonly Func<string> getHomeDirectory;

    public XdgApplicationPaths()
        : this(Environment.GetEnvironmentVariable, GetHomeDirectory)
    {
    }

    internal XdgApplicationPaths(ApplicationPaths paths)
    {
        getEnvironmentVariable = _ => null;
        getHomeDirectory = () => paths.ConfigDirectory;
        Current = paths;
    }

    internal XdgApplicationPaths(
        Func<string, string?> getEnvironmentVariable,
        Func<string> getHomeDirectory)
    {
        this.getEnvironmentVariable = getEnvironmentVariable;
        this.getHomeDirectory = getHomeDirectory;
        Current = Resolve();
    }

    public ApplicationPaths Current { get; }

    private ApplicationPaths Resolve()
    {
        string home = getHomeDirectory();
        string config = ResolveDirectory("XDG_CONFIG_HOME", Path.Combine(home, ".config"));
        string data = ResolveDirectory("XDG_DATA_HOME", Path.Combine(home, ".local", "share"));
        string state = ResolveDirectory("XDG_STATE_HOME", Path.Combine(home, ".local", "state"));
        string cache = ResolveDirectory("XDG_CACHE_HOME", Path.Combine(home, ".cache"));

        return new(
            config,
            data,
            state,
            cache,
            Path.Combine(data, "harness.db"),
            Path.Combine(state, "logs"),
            Path.Combine(state, "worktrees"));
    }

    private string ResolveDirectory(string variable, string fallback)
    {
        string? configured = getEnvironmentVariable(variable);
        string root = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return Path.GetFullPath(Path.Combine(root, ApplicationDirectoryName));
    }

    private static string GetHomeDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
