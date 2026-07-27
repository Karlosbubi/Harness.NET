namespace Harness.BusinessLogic.Dashboard;

public sealed record DashboardSnapshot(
    WorkspaceSummary Workspace,
    string Goal,
    IReadOnlyList<ActivityItem> Activities,
    IReadOnlyList<string> Plan,
    string Diff,
    IReadOnlyList<EvidenceItem> Evidence,
    ProviderSnapshot Provider,
    string Status,
    string Budget);
