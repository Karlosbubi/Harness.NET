using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia;

internal sealed record AvaloniaShellState(
    DashboardSnapshot? Dashboard,
    AppearanceSnapshot? Appearance,
    ApplicationSettingsState Settings,
    WorkspaceManagementState Workspaces,
    FrameworkManagementState Framework,
    GoalManagementState Goals,
    ApplicationOperationsState Operations,
    bool IsLoading,
    bool IsStreaming,
    string ComposerText,
    string? Error)
{
    internal static AvaloniaShellState Initial { get; } = new(
        null,
        null,
        ApplicationSettingsState.Initial,
        WorkspaceManagementState.Initial,
        FrameworkManagementState.Initial,
        GoalManagementState.Initial,
        ApplicationOperationsState.Initial,
        IsLoading: true,
        IsStreaming: false,
        string.Empty,
        null);
}

internal sealed record ApplicationSettingsState(
    AgentDefaultsSnapshot? AgentDefaults,
    bool IsBusy,
    string? Status)
{
    internal static ApplicationSettingsState Initial { get; } = new(
        AgentDefaults: null,
        IsBusy: false,
        Status: null);
}

internal sealed record FrameworkManagementState(
    FrameworkSnapshot? Snapshot,
    bool IsBusy,
    string? Status)
{
    internal static FrameworkManagementState Initial { get; } = new(
        Snapshot: null,
        IsBusy: false,
        Status: null);
}

internal sealed record GoalManagementState(
    IReadOnlyList<GoalView> Items,
    GoalId? SelectedGoalId,
    PlanView? CurrentPlan,
    GoalModelCatalog? ModelCatalog,
    IReadOnlyList<GoalModelSelectionView> ModelSelections,
    RemoteCostReport? Cost,
    GoalWorkflowSnapshot? Workflow,
    SemanticIndexStatusResult? SemanticStatus,
    SemanticIndexResult? SemanticRebuild,
    SemanticSearchResult? SemanticSearch,
    GoalCommitPreview? CommitPreview,
    GoalCommitApprovalView? CommitApproval,
    IReadOnlyList<CapabilityApprovalView> CapabilityApprovals,
    bool IsBusy,
    bool IsWorkflowRunning,
    bool IsSemanticRunning,
    string? Status)
{
    internal static GoalManagementState Initial { get; } = new(
        Items: [],
        SelectedGoalId: null,
        CurrentPlan: null,
        ModelCatalog: null,
        ModelSelections: [],
        Cost: null,
        Workflow: null,
        SemanticStatus: null,
        SemanticRebuild: null,
        SemanticSearch: null,
        CommitPreview: null,
        CommitApproval: null,
        CapabilityApprovals: [],
        IsBusy: false,
        IsWorkflowRunning: false,
        IsSemanticRunning: false,
        Status: null);

    internal GoalView? SelectedGoal => SelectedGoalId is null
        ? null
        : Items.FirstOrDefault(goal => goal.Id == SelectedGoalId);
}

internal sealed record ApplicationOperationsState(
    bool IsBusy,
    ApplicationBackupView? LastBackup,
    ApplicationRestoreView? InspectedRestore,
    ApplicationRestoreView? PendingRestore,
    string? Status)
{
    internal static ApplicationOperationsState Initial { get; } = new(
        IsBusy: false,
        LastBackup: null,
        InspectedRestore: null,
        PendingRestore: null,
        Status: null);
}

internal sealed record WorkspaceManagementState(
    IReadOnlyList<WorkspaceView> Registered,
    string RepositoryPath,
    IReadOnlyList<string> EntryPoints,
    bool IsBusy,
    string? Status)
{
    internal static WorkspaceManagementState Initial { get; } = new(
        [],
        string.Empty,
        [],
        IsBusy: false,
        Status: null);
}
