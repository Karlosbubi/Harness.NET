namespace Harness.BusinessLogic.Dashboard;

public sealed record WorkspaceSummary(
    string Name,
    string Path,
    string Branch,
    string Trust);
