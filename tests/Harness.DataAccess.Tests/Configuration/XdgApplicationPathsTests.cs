using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.Tests.Configuration;

public sealed class XdgApplicationPathsTests
{
    [Fact]
    public void Uses_configured_xdg_roots()
    {
        Dictionary<string, string> environment = new()
        {
            ["XDG_CONFIG_HOME"] = "/xdg/config",
            ["XDG_DATA_HOME"] = "/xdg/data",
            ["XDG_STATE_HOME"] = "/xdg/state",
            ["XDG_CACHE_HOME"] = "/xdg/cache",
        };

        XdgApplicationPaths paths = new(
            name => environment.GetValueOrDefault(name),
            () => "/home/developer");

        Assert.Equal("/xdg/config/harness.net", paths.Current.ConfigDirectory);
        Assert.Equal("/xdg/data/harness.net/harness.db", paths.Current.DatabasePath);
        Assert.Equal("/xdg/state/harness.net/logs", paths.Current.LogDirectory);
        Assert.Equal("/xdg/state/harness.net/worktrees", paths.Current.WorktreeDirectory);
        Assert.Equal("/xdg/cache/harness.net", paths.Current.CacheDirectory);
    }

    [Fact]
    public void Falls_back_to_freedesktop_defaults()
    {
        XdgApplicationPaths paths = new(_ => null, () => "/home/developer");

        Assert.Equal("/home/developer/.config/harness.net", paths.Current.ConfigDirectory);
        Assert.Equal("/home/developer/.local/share/harness.net", paths.Current.DataDirectory);
        Assert.Equal("/home/developer/.local/state/harness.net", paths.Current.StateDirectory);
        Assert.Equal("/home/developer/.cache/harness.net", paths.Current.CacheDirectory);
    }
}
