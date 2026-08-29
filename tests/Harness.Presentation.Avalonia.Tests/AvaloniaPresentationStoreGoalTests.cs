using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class AvaloniaPresentationStoreTests
{
    [Fact]
    public async Task Creates_goal_and_drives_plan_approval_through_business_boundary()
    {
        WorkspaceService workspaces = new();
        GoalService goals = new();
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
            new CapabilityApprovalService(),
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
            "Ship desktop workflow",
            "Expose the plan lifecycle in Avalonia.",
            new(3),
            new MicroUsdAmount(2_000_000)), CancellationToken.None);
        GoalView goal = Assert.Single(store.Current.Goals.Items);
        await store.ProposePlanAsync(goal.Id, "1. Implement\n2. Verify", CancellationToken.None);
        await store.DecidePlanAsync(
            goal.Id,
            PlanDecision.Approve,
            reason: null,
            CancellationToken.None);

        Assert.Equal(GoalState.Approved, store.Current.Goals.SelectedGoal?.State);
        Assert.Equal(PlanState.Approved, store.Current.Goals.CurrentPlan?.State);
        Assert.Equal(PlanDecision.Approve, goals.LastDecision);
        Assert.Contains("isolated branch", store.Current.Goals.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Selects_role_route_and_streams_bounded_workflow_snapshots()
    {
        WorkspaceService workspaces = new();
        GoalService goals = new();
        GoalModelService models = new();
        GoalWorkflowService workflow = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            workspaces,
            goals,
            models,
            new AgentDefaultsService(),
            new RemoteCostService(),
            workflow,
            new SemanticIndexService(),
            new GoalAcceptanceService(),
            new ApplicationOperationsService(),
            new CapabilityApprovalService(),
            new FrameworkService(),
            NullLogger<AvaloniaPresentationStore>.Instance);
        await store.LoadAsync(CancellationToken.None);
        store.SetRepositoryPath("/work/repository");
        await store.InspectWorkspaceAsync(CancellationToken.None);
        await store.RegisterWorkspaceAsync(
            Assert.Single(store.Current.Workspaces.EntryPoints),
            CancellationToken.None);
        WorkspaceView workspace = Assert.Single(store.Current.Workspaces.Registered);
        await store.CreateGoalAsync(new(
            workspace.Id,
            "Run production workflow",
            "Plan and implement through bounded roles.",
            new(3),
            new MicroUsdAmount(2_000_000)), CancellationToken.None);
        GoalView goal = Assert.Single(store.Current.Goals.Items);

        await store.DiscoverGoalModelsAsync(goal.Id, CancellationToken.None);
        GoalModelCandidate remote = Assert.Single(
            store.Current.Goals.ModelCatalog!.Models,
            candidate => candidate.Access is ModelAccess.Remote);
        await store.StartGoalWorkflowAsync(
            goal.Id,
            remote,
            CancellationToken.None);
        await store.ResumeGoalWorkflowAsync(
            goal.Id,
            CancellationToken.None);

        Assert.Equal(remote.Model, models.Selections[AgentRole.Lead].Model);
        Assert.Equal(GoalWorkflowState.Completed, store.Current.Goals.Workflow?.State);
        Assert.False(store.Current.Goals.IsWorkflowRunning);
        ConversationWorkflowCard runCard = Assert.Single(
            ConversationWorkflowProjector.Project(store.Current.Goals),
            card => card.Id == $"run.{store.Current.Goals.Workflow?.Id.Value}");
        Assert.Equal(
            ConversationWorkflowActionKind.ReviewAcceptedChanges,
            Assert.Single(ConversationWorkflowActionProjector.Project(
                runCard,
                store.Current.Goals)).Kind);
    }

    [Fact]
    public async Task Inspects_rebuilds_and_searches_strict_goal_scoped_semantic_context()
    {
        WorkspaceService workspaces = new();
        GoalService goals = new();
        SemanticIndexService semantic = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            workspaces,
            goals,
            new GoalModelService(),
            new AgentDefaultsService(),
            new RemoteCostService(),
            new GoalWorkflowService(),
            semantic,
            new GoalAcceptanceService(),
            new ApplicationOperationsService(),
            new CapabilityApprovalService(),
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
            "Index repository",
            "Build and preview semantic context.",
            new(3),
            new MicroUsdAmount(2_000_000)), CancellationToken.None);
        GoalView goal = Assert.Single(store.Current.Goals.Items);

        await store.RefreshSemanticStatusAsync(goal.Id, CancellationToken.None);
        await store.RebuildSemanticIndexAsync(goal.Id, CancellationToken.None);
        await store.SearchSemanticContextAsync(
            goal.Id,
            "Where is the desktop workflow?",
            CancellationToken.None);

        Assert.Equal(2, semantic.StatusCalls);
        Assert.Equal(SemanticPrivacyPolicy.NoCollectionAndZeroDataRetention,
            semantic.LastIndexRequest?.PrivacyPolicy);
        Assert.Equal(goal.Id.Value, semantic.LastIndexRequest?.RemoteGoalId);
        Assert.Equal(8, semantic.LastSearchRequest?.MaximumResults);
        Assert.Single(store.Current.Goals.SemanticSearch!.Matches);
        Assert.Equal(12, store.Current.Goals.SemanticRebuild?.Usage.InputTokens);
        Assert.False(store.Current.Goals.IsSemanticRunning);
    }

    [Fact]
    public async Task Records_then_separately_approves_exact_commit_fingerprint()
    {
        WorkspaceService workspaces = new();
        GoalService goals = new();
        GoalWorkflowService workflow = new();
        GoalAcceptanceService acceptance = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            workspaces,
            goals,
            new GoalModelService(),
            new AgentDefaultsService(),
            new RemoteCostService(),
            workflow,
            new SemanticIndexService(),
            acceptance,
            new ApplicationOperationsService(),
            new CapabilityApprovalService(),
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
            "Commit accepted work",
            "Record and approve an exact local commit.",
            new(3),
            RemoteBudget: null), CancellationToken.None);
        GoalView goal = Assert.Single(store.Current.Goals.Items);
        await store.ProposePlanAsync(goal.Id, "Implement and verify.", CancellationToken.None);
        await store.DecidePlanAsync(goal.Id, PlanDecision.Approve, null, CancellationToken.None);
        await store.ResumeGoalWorkflowAsync(goal.Id, CancellationToken.None);

        await store.RefreshCommitAsync(goal.Id, CancellationToken.None);
        GoalCommitPreview preview = Assert.IsType<GoalCommitPreview>(store.Current.Goals.CommitPreview);
        await store.RequestCommitApprovalAsync(
            new("feat: complete desktop workflow"),
            new("Harness User"),
            new("user@example.test"),
            CancellationToken.None);

        Assert.Equal(GoalCommitApprovalState.Pending, store.Current.Goals.CommitApproval?.State);
        Assert.Equal(preview.DiffHash, store.Current.Goals.CommitApproval?.DiffHash);
        Assert.Equal(0, acceptance.DecisionCalls);
        ConversationWorkflowCard commitCard = Assert.Single(
            ConversationWorkflowProjector.Project(store.Current.Goals),
            card => card.Kind is ConversationWorkflowCardKind.CommitApproval);
        Assert.Equal(
            [
                ConversationWorkflowActionKind.ApproveCommit,
                ConversationWorkflowActionKind.DenyCommit,
            ],
            ConversationWorkflowActionProjector.Project(commitCard, store.Current.Goals)
                .Select(action => action.Kind));

        await store.DecideCommitAsync(
            GoalCommitDecision.Approve,
            reason: null,
            CancellationToken.None);

        Assert.Equal(1, acceptance.DecisionCalls);
        Assert.Equal(GoalCommitApprovalState.Committed, store.Current.Goals.CommitApproval?.State);
        Assert.Equal(new GoalCommitHead(new string('b', 40)),
            store.Current.Goals.CommitApproval?.CommitSha);
        ConversationWorkflowCard handoff = Assert.Single(
            ConversationWorkflowProjector.Project(store.Current.Goals),
            card => card.Kind is ConversationWorkflowCardKind.Handoff);
        Assert.Contains("harness/goal", handoff.Summary, StringComparison.Ordinal);
        Assert.Contains("local only", handoff.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("will not push", handoff.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ConversationWorkflowActionKind.ReviewBranchHandoff,
            Assert.Single(ConversationWorkflowActionProjector.Project(
                handoff, store.Current.Goals)).Kind);
    }

}
