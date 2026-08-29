using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class AvaloniaPresentationStoreTests
{
    [Fact]
    public async Task Creates_verified_application_state_backup()
    {
        ApplicationOperationsService operations = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            new WorkspaceService(),
            new GoalService(),
            new GoalModelService(),
            new AgentDefaultsService(),
            new RemoteCostService(),
            new GoalWorkflowService(),
            new SemanticIndexService(),
            new GoalAcceptanceService(),
            operations,
            new CapabilityApprovalService(),
            new FrameworkService(),
            NullLogger<AvaloniaPresentationStore>.Instance);

        await store.CreateApplicationBackupAsync(
            new("/backups/harness-state.zip"),
            CancellationToken.None);

        Assert.Equal("/backups/harness-state.zip", operations.LastDestination?.Value);
        Assert.Equal(18, store.Current.Operations.LastBackup?.SchemaVersion.Value);
        Assert.Contains("Verified backup", store.Current.Operations.Status, StringComparison.Ordinal);
        Assert.False(store.Current.Operations.IsBusy);
    }

    [Fact]
    public async Task Inspects_then_stages_restore_for_restart()
    {
        ApplicationOperationsService operations = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(), new AppearanceService(), new WorkspaceService(),
            new GoalService(), new GoalModelService(), new AgentDefaultsService(),
            new RemoteCostService(), new GoalWorkflowService(), new SemanticIndexService(),
            new GoalAcceptanceService(), operations, new CapabilityApprovalService(),
            new FrameworkService(), NullLogger<AvaloniaPresentationStore>.Instance);

        RestoreSourcePath source = new("/backups/restore.zip");
        await store.InspectApplicationRestoreAsync(source, CancellationToken.None);
        await store.StageApplicationRestoreAsync(
            store.Current.Operations.InspectedRestore!, CancellationToken.None);

        Assert.Equal(source, operations.LastRestoreSource);
        Assert.Equal(21, store.Current.Operations.PendingRestore?.SchemaVersion.Value);
        Assert.Contains("restart", store.Current.Operations.Status,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(store.Current.Operations.IsBusy);
    }

    [Fact]
    public async Task Records_and_separately_approves_correlation_bound_restore()
    {
        WorkspaceService workspaces = new();
        GoalService goals = new();
        CapabilityApprovalService approvals = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            workspaces,
            goals,
            new GoalModelService(),
            new AgentDefaultsService(),
            new RemoteCostService(),
            new GoalWorkflowService(),
            new SemanticIndexService(),
            new GoalAcceptanceService(),
            new ApplicationOperationsService(),
            approvals,
            new FrameworkService(),
            NullLogger<AvaloniaPresentationStore>.Instance);
        await store.LoadAsync(CancellationToken.None);
        store.SetRepositoryPath("/work/repository");
        await store.InspectWorkspaceAsync(CancellationToken.None);
        await store.RegisterWorkspaceAsync(
            Assert.Single(store.Current.Workspaces.EntryPoints),
            CancellationToken.None);
        WorkspaceView workspace = Assert.Single(store.Current.Workspaces.Registered);
        await store.SetWorkspaceTrustAsync(workspace.Id, true, CancellationToken.None);
        await store.CreateGoalAsync(new(
            workspace.Id,
            "Restore dependencies",
            "Approve one exact restore call.",
            new(3),
            RemoteBudget: null), CancellationToken.None);
        GoalView goal = Assert.Single(store.Current.Goals.Items);
        await store.ProposePlanAsync(goal.Id, "Restore and build.", CancellationToken.None);
        await store.DecidePlanAsync(goal.Id, PlanDecision.Approve, null, CancellationToken.None);
        ToolCorrelationId correlation = new("restore-call-1");

        await store.RequestRestoreApprovalAsync(
            goal.Id,
            correlation,
            "Resolve locked project dependencies.",
            CancellationToken.None);
        CapabilityApprovalView pending = Assert.Single(store.Current.Goals.CapabilityApprovals);

        Assert.Equal(CapabilityApprovalState.Pending, pending.State);
        Assert.Equal(correlation, pending.CorrelationId);
        Assert.Equal("Harness.slnx", pending.Target);
        Assert.Equal(0, approvals.DecisionCalls);
        ConversationWorkflowCard restoreCard = Assert.Single(
            ConversationWorkflowProjector.Project(store.Current.Goals),
            card => card.Kind is ConversationWorkflowCardKind.CapabilityApproval);
        Assert.Equal(
            [
                ConversationWorkflowActionKind.ApproveRestore,
                ConversationWorkflowActionKind.DenyRestore,
            ],
            ConversationWorkflowActionProjector.Project(restoreCard, store.Current.Goals)
                .Select(action => action.Kind));

        await store.DecideRestoreApprovalAsync(
            goal.Id,
            pending.Id,
            CapabilityDecision.Approve,
            reason: null,
            CancellationToken.None);

        Assert.Equal(1, approvals.DecisionCalls);
        Assert.Equal(CapabilityApprovalState.Approved,
            Assert.Single(store.Current.Goals.CapabilityApprovals).State);
    }

}
