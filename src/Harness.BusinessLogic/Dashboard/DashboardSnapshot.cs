namespace Harness.BusinessLogic.Dashboard;

public sealed record DashboardSnapshot(
    WorkspaceSummary Workspace,
    IReadOnlyList<ActivityItem> Activities,
    ProviderSnapshot Provider,
    string Status,
    string Budget);
