namespace Harness.Presentation.Terminal;

internal enum ShellLayoutMode
{
    Narrow,
    Compact,
    Wide,
}

internal sealed record ShellLayout(
    ShellLayoutMode Mode,
    bool ShowWorkspace,
    bool ShowDetails);

internal static class ShellLayoutPolicy
{
    internal static ShellLayout ForWidth(int width) => width switch
    {
        >= 120 => new(ShellLayoutMode.Wide, ShowWorkspace: true, ShowDetails: true),
        >= 80 => new(ShellLayoutMode.Compact, ShowWorkspace: true, ShowDetails: false),
        _ => new(ShellLayoutMode.Narrow, ShowWorkspace: false, ShowDetails: false),
    };
}
