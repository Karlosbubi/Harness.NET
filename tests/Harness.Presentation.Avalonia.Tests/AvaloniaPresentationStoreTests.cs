using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.Presentation.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AvaloniaPresentationStoreTests
{
    /// <summary>Builds a store over the deterministic fakes so other suites can drive real dialogs.</summary>
    internal static AvaloniaPresentationStore CreateStore() => new(
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
        new ApplicationOperationsService(),
        new CapabilityApprovalService(),
        new FrameworkService(),
        NullLogger<AvaloniaPresentationStore>.Instance);

    [Theory]
    [InlineData("I am **Gemma 4** 😊</blockquote>", "I am Gemma 4 😊")]
    [InlineData("# Result\n\n> Useful text", "Result\nUseful text")]
    [InlineData("```csharp\nvar value = 1;\n```", "var value = 1;")]
    public void Formats_provider_markdown_without_leaking_markup(
        string source,
        string expected) =>
        Assert.Equal(expected, ConversationContentFormatter.ToReadableText(source));

    [Fact]
    public async Task Loads_and_reduces_streaming_snapshots()
    {
        DashboardService dashboard = new();
        AppearanceService appearance = new();
        using AvaloniaPresentationStore store = new(
            dashboard,
            appearance,
            new WorkspaceService(),
            new GoalService(),
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
        store.SetComposerText("hello");
        await store.SubmitAsync(CancellationToken.None);

        Assert.False(store.Current.IsLoading);
        Assert.False(store.Current.IsStreaming);
        Assert.Equal(string.Empty, store.Current.ComposerText);
        Assert.Equal("Ready after stream", store.Current.Dashboard?.Status);
        Assert.Equal("hello", dashboard.LastInstruction);
    }

    [Fact]
    public async Task First_composer_submission_creates_private_goal_without_calling_provider()
    {
        DashboardService dashboard = new();
        GoalService goals = new();
        using AvaloniaPresentationStore store = new(
            dashboard,
            new AppearanceService(),
            new WorkspaceService(),
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
        store.SetComposerText(
            "Build a chat-first goal workflow with deterministic authority boundaries and clear feedback.");

        await store.SubmitComposerAsync(CancellationToken.None);

        GoalView goal = Assert.Single(store.Current.Goals.Items);
        Assert.Equal(
            "Build a chat-first goal workflow with deterministic authority…",
            goal.Title);
        Assert.Equal(new ReviewCycleLimit(3), goal.ReviewCycleLimit);
        Assert.Null(goal.RemoteBudget);
        Assert.Equal(goal.Id, store.Current.Goals.SelectedGoalId);
        Assert.Equal(string.Empty, store.Current.ComposerText);
        Assert.Null(dashboard.LastInstruction);

        ConversationWorkflowCard goalCard = Assert.Single(
            ConversationWorkflowProjector.Project(store.Current.Goals),
            card => card.Kind is ConversationWorkflowCardKind.Goal);
        Assert.Equal(
            ConversationWorkflowActionKind.ConfigureGoal,
            Assert.Single(ConversationWorkflowActionProjector.Project(
                goalCard,
                store.Current.Goals)).Kind);

        await store.UpdateGoalSettingsAsync(new(
            goal.Id,
            new(5),
            new MicroUsdAmount(2_000_000),
            goal.UpdatedAt), CancellationToken.None);

        Assert.Equal(new ReviewCycleLimit(5), store.Current.Goals.SelectedGoal?.ReviewCycleLimit);
        Assert.Equal(new MicroUsdAmount(2_000_000), store.Current.Goals.SelectedGoal?.RemoteBudget);
        Assert.Null(dashboard.LastInstruction);

        await store.ExtendGoalBudgetAsync(new(
            goal.Id,
            new(2_000_000),
            new(3_000_000),
            new("Explicit recovery increase.")), CancellationToken.None);

        Assert.Equal(new MicroUsdAmount(3_000_000),
            store.Current.Goals.SelectedGoal?.RemoteBudget);
        Assert.Contains("does not retry", store.Current.Goals.Status,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task First_composer_submission_requires_trusted_workspace()
    {
        using AvaloniaPresentationStore store = CreateStore();
        await store.LoadAsync(CancellationToken.None);
        store.SetRepositoryPath("/work/repository");
        await store.InspectWorkspaceAsync(CancellationToken.None);
        await store.RegisterWorkspaceAsync(
            Assert.Single(store.Current.Workspaces.EntryPoints),
            CancellationToken.None);
        store.SetComposerText("Create a safe goal");

        await store.SubmitComposerAsync(CancellationToken.None);

        Assert.Empty(store.Current.Goals.Items);
        Assert.Equal("Create a safe goal", store.Current.ComposerText);
        Assert.Contains("Trust", store.Current.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selects_theme_through_business_boundary()
    {
        AppearanceService appearance = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            appearance,
            new WorkspaceService(),
            new GoalService(),
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

        await store.SelectThemeAsync("harness.dark", CancellationToken.None);

        Assert.Equal("harness.dark", appearance.Selected.Value);
        Assert.Equal("harness.dark", store.Current.Appearance?.PreferredThemeId.Value);
    }

    [Fact]
    public async Task Switching_workspaces_restores_each_selected_goal_context()
    {
        WorkspaceView first = new(
            "workspace-1", "/work/first", "First", "/work/first/First.slnx",
            IsTrusted: true, IsActive: true, "main", IsDirty: false);
        WorkspaceView second = new(
            "workspace-2", "/work/second", "Second", "/work/second/Second.slnx",
            IsTrusted: true, IsActive: false, "feature", IsDirty: false);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GoalView firstGoal = new(
            new("goal-first"), first.Id, "First goal", "First objective", new(3), null,
            GoalState.Draft, now, now);
        GoalView secondGoal = new(
            new("goal-second"), second.Id, "Second goal", "Second objective", new(3), null,
            GoalState.Draft, now, now);
        MultiWorkspaceService workspaces = new([first, second]);
        MultiGoalService goals = new([firstGoal, secondGoal]);
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
        await store.SelectGoalAsync(firstGoal.Id, CancellationToken.None);

        await store.SelectWorkspaceAsync(second.Id, CancellationToken.None);
        Assert.Null(store.Current.Goals.SelectedGoalId);
        await store.SelectGoalAsync(secondGoal.Id, CancellationToken.None);
        await store.SelectWorkspaceAsync(first.Id, CancellationToken.None);

        Assert.Equal(firstGoal.Id, store.Current.Goals.SelectedGoalId);
        Assert.Equal(firstGoal, store.Current.Goals.SelectedGoal);
        await store.SelectWorkspaceAsync(second.Id, CancellationToken.None);
        Assert.Equal(secondGoal.Id, store.Current.Goals.SelectedGoalId);
    }

    [Fact]
    public async Task Discovers_and_persists_agent_defaults_through_typed_boundary()
    {
        AgentDefaultsService defaults = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            new WorkspaceService(),
            new GoalService(),
            new GoalModelService(),
            defaults,
            new RemoteCostService(),
            new GoalWorkflowService(),
            new SemanticIndexService(),
            new GoalAcceptanceService(),
            new ApplicationOperationsService(),
            new CapabilityApprovalService(),
            new FrameworkService(),
            NullLogger<AvaloniaPresentationStore>.Instance);
        await store.LoadAsync(CancellationToken.None);

        await store.DiscoverAgentDefaultsAsync(CancellationToken.None);
        GoalModelCandidate candidate = Assert.Single(
            store.Current.Settings.AgentDefaults!.Models);
        await store.UpdateAgentDefaultAsync(
            AgentRole.Reviewer,
            candidate,
            4096,
            CancellationToken.None);

        AgentRoleDefault reviewer = store.Current.Settings.AgentDefaults!.Roles
            .Single(item => item.Role is AgentRole.Reviewer);
        Assert.True(reviewer.IsPersisted);
        Assert.Equal(4096, reviewer.MaximumOutputTokens.Value);
        Assert.Equal("Saved Reviewer defaults.", store.Current.Settings.Status);
    }

    [Fact]
    public async Task Registers_selects_and_explicitly_trusts_workspace()
    {
        WorkspaceService workspaces = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            workspaces,
            new GoalService(),
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
        string entryPoint = Assert.Single(store.Current.Workspaces.EntryPoints);
        await store.RegisterWorkspaceAsync(entryPoint, CancellationToken.None);
        WorkspaceView registered = Assert.Single(store.Current.Workspaces.Registered);
        await store.SetWorkspaceTrustAsync(registered.Id, true, CancellationToken.None);

        Assert.True(Assert.Single(store.Current.Workspaces.Registered).IsTrusted);
        Assert.Equal(registered.Id, workspaces.SelectedId);
        Assert.Contains("Trusted", store.Current.Workspaces.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workspace_folder_selection_populates_the_path_and_scans_entry_points()
    {
        WorkspaceService workspaces = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            workspaces,
            new GoalService(),
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
        FolderPicker picker = new("/work/repository");

        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkspaceDialog dialog = new(store, CancellationToken.None, picker);
            dialog.Show();
            dialog.BrowseRepositoryAsync().GetAwaiter().GetResult();

            Assert.Same(dialog, picker.Owner);
            Assert.Equal("/work/repository", store.Current.Workspaces.RepositoryPath);
            Assert.Single(store.Current.Workspaces.EntryPoints);
            Assert.Contains("Found 1", store.Current.Workspaces.Status, StringComparison.Ordinal);
            dialog.Close();
        }, CancellationToken.None);
    }

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
        await store.SelectGoalModelAsync(
            goal.Id,
            AgentRole.Lead,
            remote,
            CancellationToken.None);
        await store.StartGoalWorkflowAsync(
            goal.Id,
            new(1024),
            CancellationToken.None);
        await store.ResumeGoalWorkflowAsync(
            goal.Id,
            new(2048),
            new(1536),
            CancellationToken.None);

        Assert.Equal(remote.Model, models.Selections[AgentRole.Lead].Model);
        Assert.Equal(1024, workflow.LeadMaximum);
        Assert.Equal(2048, workflow.ImplementerMaximum);
        Assert.Equal(1536, workflow.ReviewerMaximum);
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
        await store.ResumeGoalWorkflowAsync(goal.Id, new(1024), new(1024), CancellationToken.None);

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
    }

    [Fact]
    public async Task Inspects_effective_framework_and_updates_only_private_overlay()
    {
        WorkspaceService workspaces = new();
        FrameworkService framework = new();
        using AvaloniaPresentationStore store = new(
            new DashboardService(),
            new AppearanceService(),
            workspaces,
            new GoalService(),
            new GoalModelService(),
            new AgentDefaultsService(),
            new RemoteCostService(),
            new GoalWorkflowService(),
            new SemanticIndexService(),
            new GoalAcceptanceService(),
            new ApplicationOperationsService(),
            new CapabilityApprovalService(),
            framework,
            NullLogger<AvaloniaPresentationStore>.Instance);
        await store.LoadAsync(CancellationToken.None);
        store.SetRepositoryPath("/work/repository");
        await store.InspectWorkspaceAsync(CancellationToken.None);
        await store.RegisterWorkspaceAsync(
            Assert.Single(store.Current.Workspaces.EntryPoints),
            CancellationToken.None);

        await store.RefreshFrameworkAsync(CancellationToken.None);
        FrameworkSnapshot initial = Assert.IsType<FrameworkSnapshot>(store.Current.Framework.Snapshot);
        Assert.Contains(initial.Documents, document => document.Source == "AGENTS.md");

        await store.SetPrivateFrameworkOverlayAsync(
            "Prefer immutable records.",
            CancellationToken.None);

        FrameworkDocumentView overlay = Assert.Single(
            store.Current.Framework.Snapshot!.Documents,
            document => document.Layer == "private-workspace");
        Assert.True(overlay.IsPrivate);
        Assert.Equal("Prefer immutable records.", overlay.Content);
        Assert.Equal("workspace-1", framework.LastWorkspaceId);
        Assert.Equal("/work/repository", framework.LastWorkspaceRoot);
    }

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

    private sealed class DashboardService : IDashboardService
    {
        internal string? LastInstruction { get; private set; }

        public ValueTask<DashboardSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Snapshot("Ready"));

        public async IAsyncEnumerable<DashboardSnapshot> SubmitAsync(
            string instruction,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastInstruction = instruction;
            yield return Snapshot("Streaming");
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return Snapshot("Ready after stream");
        }

        public ValueTask<DashboardSnapshot> RefreshProviderAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Snapshot("Provider refreshed"));

        public ValueTask<DashboardSnapshot> SelectModelAsync(
            string model,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Snapshot($"Selected {model}"));

        private static DashboardSnapshot Snapshot(string status) => new(
            new("Harness", "/workspace", "main", "Trusted"),
            [new("Assistant", status, "Complete")],
            new("Ollama", "Ready", "gemma4", [new("gemma4", null, null, null, [])], null),
            status,
            "Local model");
    }

    private sealed class AppearanceService : IAppearanceService
    {
        internal ThemeId Selected { get; private set; } = new("system");

        public ValueTask<AppearanceSnapshot> GetAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Snapshot());

        public ValueTask<AppearanceSelectionResult> SelectAsync(
            ThemeId themeId,
            CancellationToken cancellationToken = default)
        {
            Selected = themeId;
            return ValueTask.FromResult(new AppearanceSelectionResult(Snapshot(), true, null));
        }

        private AppearanceSnapshot Snapshot() => new(
            Selected,
            Selected,
            [
                new(new("system"), "System", ThemeBaseVariant.System, ThemeOrigin.BuiltIn,
                    new Dictionary<ThemeColorToken, ThemeColorValue>()),
                new(new("harness.dark"), "Harness Dark", ThemeBaseVariant.Dark,
                    ThemeOrigin.BuiltIn, new Dictionary<ThemeColorToken, ThemeColorValue>()),
            ],
            []);
    }

    private sealed class WorkspaceService : IWorkspaceService
    {
        private WorkspaceView? workspace;

        internal string? SelectedId { get; private set; }

        public ValueTask<WorkspaceResult> InspectAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkspaceResult(
                workspace,
                [Path.Combine(path, "Harness.slnx")],
                null));

        public ValueTask<WorkspaceResult> RegisterAsync(
            string path,
            string entryPoint,
            CancellationToken cancellationToken = default)
        {
            workspace = new(
                "workspace-1",
                path,
                "Repository",
                entryPoint,
                IsTrusted: false,
                IsActive: true,
                "main",
                IsDirty: false);
            SelectedId = workspace.Id;
            return ValueTask.FromResult(new WorkspaceResult(workspace, [entryPoint], null));
        }

        public ValueTask<WorkspaceResult> SetTrustAsync(
            string workspaceId,
            bool isTrusted,
            CancellationToken cancellationToken = default)
        {
            workspace = (workspace ?? throw new InvalidOperationException()) with
            {
                IsTrusted = isTrusted,
            };
            return ValueTask.FromResult(new WorkspaceResult(workspace, [workspace.EntryPoint], null));
        }

        public ValueTask<IReadOnlyList<WorkspaceView>> ListAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<WorkspaceView>>(
                workspace is null ? [] : [workspace]);

        public ValueTask<WorkspaceView?> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(workspace);

        public ValueTask<WorkspaceView> SelectAsync(
            string workspaceId,
            CancellationToken cancellationToken = default)
        {
            SelectedId = workspaceId;
            workspace = (workspace ?? throw new InvalidOperationException()) with { IsActive = true };
            return ValueTask.FromResult(workspace);
        }
    }

    private sealed class MultiWorkspaceService(IReadOnlyList<WorkspaceView> initial)
        : IWorkspaceService
    {
        private IReadOnlyList<WorkspaceView> workspaces = initial;

        public ValueTask<IReadOnlyList<WorkspaceView>> ListAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(workspaces);

        public ValueTask<WorkspaceView?> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(workspaces.FirstOrDefault(item => item.IsActive));

        public ValueTask<WorkspaceView> SelectAsync(
            string workspaceId,
            CancellationToken cancellationToken = default)
        {
            workspaces = workspaces.Select(item => item with
            {
                IsActive = item.Id.Equals(workspaceId, StringComparison.Ordinal),
            }).ToArray();
            return ValueTask.FromResult(workspaces.Single(item => item.IsActive));
        }

        public ValueTask<WorkspaceResult> InspectAsync(
            string path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkspaceResult> RegisterAsync(
            string path,
            string entryPoint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkspaceResult> SetTrustAsync(
            string workspaceId,
            bool isTrusted,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MultiGoalService(IReadOnlyList<GoalView> goals) : IGoalService
    {
        public ValueTask<GoalView?> GetAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(goals.FirstOrDefault(goal => goal.Id == goalId));

        public ValueTask<IReadOnlyList<GoalView>> ListAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<GoalView>>(
                goals.Where(goal => goal.WorkspaceId == workspaceId).ToArray());

        public ValueTask<PlanView?> GetCurrentPlanAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<PlanView?>(null);

        public ValueTask<GoalResult> CreateAsync(
            GoalCreateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GoalResult> UpdateSettingsAsync(
            GoalSettingsUpdateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GoalBudgetExtensionResult> ExtendRemoteBudgetAsync(
            GoalBudgetExtensionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<PlanResult> ProposePlanAsync(
            PlanProposalRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<PlanResult> DecidePlanAsync(
            PlanDecisionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FrameworkService : IFrameworkService
    {
        private string? overlay;

        internal string? LastWorkspaceId { get; private set; }
        internal string? LastWorkspaceRoot { get; private set; }

        public ValueTask<FrameworkSnapshot> GetEffectiveAsync(
            string workspaceId,
            string workspaceRoot,
            CancellationToken cancellationToken = default)
        {
            LastWorkspaceId = workspaceId;
            LastWorkspaceRoot = workspaceRoot;
            List<FrameworkDocumentView> documents =
            [
                new("repository", 20, "AGENTS.md", "Use explicit boundaries.", IsPrivate: false),
            ];
            if (overlay is not null)
            {
                documents.Add(new(
                    "private-workspace",
                    30,
                    "Harness.NET private workspace overlay",
                    overlay,
                    IsPrivate: true));
            }

            return ValueTask.FromResult(new FrameworkSnapshot(
                documents,
                [new("nullable", "enabled", "global", IsLocked: true, "defaults.xml")],
                []));
        }

        public ValueTask<FrameworkSnapshot> SetPrivateOverlayAsync(
            string workspaceId,
            string workspaceRoot,
            string? content,
            CancellationToken cancellationToken = default)
        {
            overlay = string.IsNullOrWhiteSpace(content) ? null : content;
            return GetEffectiveAsync(workspaceId, workspaceRoot, cancellationToken);
        }
    }

    private sealed class GoalService : IGoalService
    {
        private GoalView? goal;
        private PlanView? plan;

        internal PlanDecision? LastDecision { get; private set; }

        public ValueTask<GoalResult> CreateAsync(
            GoalCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            goal = new(
                new("goal-1"),
                request.WorkspaceId,
                request.Title,
                request.Objective,
                request.ReviewCycleLimit,
                request.RemoteBudget,
                GoalState.Draft,
                now,
                now);
            return ValueTask.FromResult(new GoalResult(goal, null, null));
        }

        public ValueTask<GoalView?> GetAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(goal);

        public ValueTask<IReadOnlyList<GoalView>> ListAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<GoalView>>(goal is null ? [] : [goal]);

        public ValueTask<GoalResult> UpdateSettingsAsync(
            GoalSettingsUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (goal is null || goal.Id != request.GoalId || goal.State is not GoalState.Draft ||
                goal.UpdatedAt != request.ExpectedUpdatedAt)
            {
                return ValueTask.FromResult(new GoalResult(
                    null, "stale_goal_settings", "The draft changed."));
            }

            goal = goal with
            {
                ReviewCycleLimit = request.ReviewCycleLimit,
                RemoteBudget = request.RemoteBudget,
                UpdatedAt = goal.UpdatedAt.AddSeconds(1),
            };
            return ValueTask.FromResult(new GoalResult(goal, null, null));
        }

        public ValueTask<GoalBudgetExtensionResult> ExtendRemoteBudgetAsync(
            GoalBudgetExtensionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (goal is null || goal.Id != request.GoalId ||
                goal.RemoteBudget != request.ExpectedBudget ||
                request.NewBudget.Value <= (goal.RemoteBudget?.Value ?? 0))
            {
                return ValueTask.FromResult(new GoalBudgetExtensionResult(
                    null, null, "stale_budget_extension", "The cap changed."));
            }

            GoalView previous = goal;
            goal = goal with
            {
                RemoteBudget = request.NewBudget,
                UpdatedAt = goal.UpdatedAt.AddSeconds(1),
            };
            return ValueTask.FromResult(new GoalBudgetExtensionResult(
                goal,
                new(new("extension-1"), goal.Id, previous.RemoteBudget,
                    request.NewBudget, request.Reason, goal.UpdatedAt),
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<PlanView?> GetCurrentPlanAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plan);

        public ValueTask<PlanResult> ProposePlanAsync(
            PlanProposalRequest request,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            goal = (goal ?? throw new InvalidOperationException()) with
            {
                State = GoalState.AwaitingPlanApproval,
                UpdatedAt = now,
            };
            plan = new(
                new("plan-1"),
                request.GoalId,
                new(1),
                request.Content,
                PlanState.Pending,
                now,
                now);
            return ValueTask.FromResult(new PlanResult(goal, plan, null, null, null, null));
        }

        public ValueTask<PlanResult> DecidePlanAsync(
            PlanDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastDecision = request.Decision;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            goal = (goal ?? throw new InvalidOperationException()) with
            {
                State = request.Decision is PlanDecision.Approve
                    ? GoalState.Approved
                    : GoalState.NeedsPlanRevision,
                UpdatedAt = now,
            };
            plan = (plan ?? throw new InvalidOperationException()) with
            {
                State = request.Decision is PlanDecision.Approve
                    ? PlanState.Approved
                    : PlanState.Denied,
                UpdatedAt = now,
            };
            GoalWorktreeView? worktree = request.Decision is PlanDecision.Approve
                ? new(
                    goal.Id,
                    goal.WorkspaceId,
                    "harness/goal-1",
                    "/worktrees/goal-1",
                    "abc123",
                    GoalWorktreeState.Active,
                    now)
                : null;
            return ValueTask.FromResult(new PlanResult(goal, plan, null, worktree, null, null));
        }
    }

    private sealed class GoalModelService : IGoalModelService
    {
        private static readonly GoalModelCandidate Local = new(
            new("ollama"),
            new("gemma4"),
            ModelAccess.Local,
            [],
            null,
            null,
            null,
            null);
        private static readonly GoalModelCandidate Remote = new(
            new("openrouter"),
            new("openai/gpt-5-mini"),
            ModelAccess.Remote,
            [],
            null,
            new(0.25m),
            new(2m),
            null);

        internal Dictionary<AgentRole, GoalModelSelectionView> Selections { get; } = [];

        public ValueTask<GoalModelCatalog> DiscoverAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GoalModelCatalog(goalId, [Local, Remote], [], null, null));

        public ValueTask<IReadOnlyList<GoalModelSelectionView>> GetSelectionsAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default)
        {
            EnsureDefaults(goalId);
            return ValueTask.FromResult<IReadOnlyList<GoalModelSelectionView>>(
                Enum.GetValues<AgentRole>().Select(role => Selections[role]).ToArray());
        }

        public ValueTask<GoalModelSelectionResult> SelectAsync(
            GoalModelSelectionRequest request,
            CancellationToken cancellationToken = default)
        {
            ModelAccess access = request.Provider == Remote.Provider
                ? ModelAccess.Remote
                : ModelAccess.Local;
            GoalModelSelectionView selection = new(
                request.GoalId,
                request.Role,
                request.Provider,
                request.Model,
                access,
                IsExplicit: true,
                DateTimeOffset.UtcNow);
            Selections[request.Role] = selection;
            return ValueTask.FromResult(new GoalModelSelectionResult(selection, null, null));
        }

        private void EnsureDefaults(GoalId goalId)
        {
            foreach (AgentRole role in Enum.GetValues<AgentRole>())
            {
                Selections.TryAdd(role, new(
                    goalId,
                    role,
                    Local.Provider,
                    Local.Model,
                    Local.Access,
                    IsExplicit: false,
                    SelectedAt: null));
            }
        }
    }

    private sealed class AgentDefaultsService : IAgentDefaultsService
    {
        private static readonly GoalModelCandidate Local = new(
            new("ollama"),
            new("gemma4"),
            ModelAccess.Local,
            [],
            null,
            null,
            null,
            null);
        private readonly Dictionary<AgentRole, AgentRoleDefault> values =
            Enum.GetValues<AgentRole>().ToDictionary(role => role, role => new AgentRoleDefault(
                role,
                Local.Provider,
                Local.Model,
                Local.Access,
                new(2048),
                IsPersisted: false,
                UpdatedAt: null));

        public ValueTask<AgentDefaultsSnapshot> GetAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Snapshot(models: []));

        public ValueTask<AgentDefaultsSnapshot> DiscoverAvailableAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Snapshot([Local]));

        public ValueTask<AgentRoleDefaultUpdateResult> UpdateAsync(
            AgentRoleDefaultUpdate request,
            CancellationToken cancellationToken = default)
        {
            AgentRoleDefault value = new(
                request.Role,
                request.Provider,
                request.Model,
                Local.Access,
                request.MaximumOutputTokens,
                IsPersisted: true,
                DateTimeOffset.UtcNow);
            values[request.Role] = value;
            return ValueTask.FromResult(new AgentRoleDefaultUpdateResult(value, null, null));
        }

        private AgentDefaultsSnapshot Snapshot(IReadOnlyList<GoalModelCandidate> models) =>
            new(values.Values.OrderBy(item => item.Role).ToArray(), models, []);
    }

    private sealed class RemoteCostService : IRemoteCostService
    {
        public ValueTask<RemoteCostReport?> GetAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RemoteCostReport?>(new(
                goalId,
                new(2_000_000),
                new(0),
                new(0),
                new(2_000_000),
                new(0),
                []));
    }

    private sealed class GoalWorkflowService : IGoalWorkflowService
    {
        private GoalWorkflowSnapshot? latest;

        internal int? LeadMaximum { get; private set; }
        internal int? ImplementerMaximum { get; private set; }
        internal int? ReviewerMaximum { get; private set; }
        internal GoalWorkflowRetryRole? RetriedRole { get; private set; }

        public ValueTask<GoalWorkflowSnapshot?> GetLatestAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(latest);

        public async IAsyncEnumerable<GoalWorkflowSnapshot> StartPlanningAsync(
            GoalWorkflowStartRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LeadMaximum = request.LeadMaximumOutputTokens.Value;
            latest = Snapshot(request.GoalId, GoalWorkflowState.Running, "Lead planning started", true);
            yield return latest;
            await Task.Yield();
            latest = Snapshot(
                request.GoalId,
                GoalWorkflowState.AwaitingPlanApproval,
                "Plan proposed",
                true);
            yield return latest;
        }

        public async IAsyncEnumerable<GoalWorkflowSnapshot> ResumeAsync(
            GoalWorkflowResumeRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ImplementerMaximum = request.ImplementerMaximumOutputTokens.Value;
            ReviewerMaximum = request.ReviewerMaximumOutputTokens.Value;
            latest = Snapshot(request.GoalId, GoalWorkflowState.Running, "Implementation started", true);
            yield return latest;
            await Task.Yield();
            latest = Snapshot(request.GoalId, GoalWorkflowState.Completed, "Accepted", false);
            yield return latest;
        }

        public async IAsyncEnumerable<GoalWorkflowSnapshot> RetryAsync(
            GoalWorkflowRetryRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RetriedRole = request.Role;
            latest = Snapshot(request.GoalId, GoalWorkflowState.Running, "Explicit retry", true);
            yield return latest;
            await Task.Yield();
        }

        private static GoalWorkflowSnapshot Snapshot(
            GoalId goalId,
            GoalWorkflowState state,
            string summary,
            bool canResume) => new(
            new("run-1"),
            goalId,
            state,
            new(0),
            [],
            [new(1, GoalWorkflowCheckpointKind.Started, WorkflowActor.System, new(summary))],
            [],
            canResume,
            RequiresUserDirection: false);
    }

    private sealed class SemanticIndexService : ISemanticIndexService
    {
        private readonly SemanticIndexProfile profile = new(
            new("openrouter"),
            new("openai/text-embedding-3-small"),
            new(1536),
            new("v1"),
            EmbeddingAccess.Remote);
        private SemanticIndexPartitionView? partition;

        internal int StatusCalls { get; private set; }
        internal SemanticIndexRequest? LastIndexRequest { get; private set; }
        internal SemanticSearchRequest? LastSearchRequest { get; private set; }

        public ValueTask<SemanticIndexStatusResult> GetStatusAsync(
            SemanticIndexRequest request,
            CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            LastIndexRequest = request;
            return ValueTask.FromResult(new SemanticIndexStatusResult(
                profile,
                partition,
                null,
                null));
        }

        public ValueTask<SemanticIndexResult> RebuildAsync(
            SemanticIndexRequest request,
            CancellationToken cancellationToken = default)
        {
            LastIndexRequest = request;
            partition = new(
                "partition-1",
                profile.Provider,
                profile.Model,
                profile.Dimensions,
                profile.ChunkingVersion,
                FileCount: 4,
                ChunkCount: 9,
                DateTimeOffset.UtcNow);
            return ValueTask.FromResult(new SemanticIndexResult(
                partition,
                TrackedFileCount: 5,
                SkippedFileCount: 1,
                IsTruncated: false,
                new(12, new(40)),
                null,
                null));
        }

        public ValueTask<SemanticSearchResult> SearchAsync(
            SemanticSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastSearchRequest = request;
            return ValueTask.FromResult(new SemanticSearchResult(
                partition,
                [new("src/App.cs", 10, 18, "internal sealed class App", new(0.125))],
                new(3, new(10)),
                null,
                null));
        }
    }

    private sealed class GoalAcceptanceService : IGoalAcceptanceService
    {
        private static readonly GoalCommitBranch Branch = new("harness/goal-1");
        private static readonly GoalCommitHead Head = new(new string('a', 40));
        private static readonly GoalCommitDiffHash Hash = new(new string('c', 64));
        private static readonly GoalCommitDiff Diff = new("diff --git a/App.cs b/App.cs\n+change");
        private GoalCommitApprovalView? approval;

        internal int DecisionCalls { get; private set; }

        public ValueTask<GoalCommitPreviewResult> PreviewAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GoalCommitPreviewResult(new(
                goalId,
                new("run-1"),
                Branch,
                Head,
                Hash,
                Diff,
                new(1)), null, null));

        public ValueTask<GoalCommitApprovalView?> GetAsync(
            GoalId goalId,
            GoalWorkflowId runId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(approval);

        public ValueTask<GoalCommitApprovalResult> RequestAsync(
            GoalCommitApprovalRequest request,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            approval = new(
                new("approval-1"),
                request.GoalId,
                request.RunId,
                Branch,
                request.ExpectedHead,
                request.ExpectedDiffHash,
                Diff,
                new(1),
                request.Message,
                request.AuthorName,
                request.AuthorEmail,
                GoalCommitApprovalState.Pending,
                null,
                null,
                now,
                null,
                null);
            return ValueTask.FromResult(new GoalCommitApprovalResult(
                approval,
                WasReconciled: false,
                null,
                null));
        }

        public ValueTask<GoalCommitApprovalResult> DecideAsync(
            GoalCommitDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            DecisionCalls++;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            approval = (approval ?? throw new InvalidOperationException()) with
            {
                State = request.Decision is GoalCommitDecision.Approve
                    ? GoalCommitApprovalState.Committed
                    : GoalCommitApprovalState.Denied,
                DecisionReason = request.Reason,
                CommitSha = request.Decision is GoalCommitDecision.Approve
                    ? new(new string('b', 40))
                    : null,
                DecidedAt = now,
                CompletedAt = request.Decision is GoalCommitDecision.Approve ? now : null,
            };
            return ValueTask.FromResult(new GoalCommitApprovalResult(
                approval,
                WasReconciled: false,
                null,
                null));
        }
    }

    private sealed class ApplicationOperationsService : IApplicationOperationsService
    {
        internal BackupDestinationPath? LastDestination { get; private set; }

        public ValueTask<ApplicationBackupResult> CreateBackupAsync(
            BackupDestinationPath destination,
            CancellationToken cancellationToken = default)
        {
            LastDestination = destination;
            return ValueTask.FromResult(new ApplicationBackupResult(new(
                destination,
                new(new string('d', 64)),
                new(new string('e', 64)),
                new(4096),
                null,
                null,
                new(18),
                DateTimeOffset.UtcNow), null, null));
        }
    }

    private sealed class CapabilityApprovalService : ICapabilityApprovalService
    {
        private readonly List<CapabilityApprovalView> approvals = [];

        internal int DecisionCalls { get; private set; }

        public ValueTask<CapabilityApprovalResult> RequestAsync(
            CapabilityApprovalRequest request,
            CancellationToken cancellationToken = default)
        {
            CapabilityApprovalView approval = new(
                new(Guid.NewGuid().ToString("N")),
                request.GoalId,
                request.CorrelationId,
                request.Capability,
                "Harness.slnx",
                request.Rationale,
                CapabilityApprovalState.Pending,
                null,
                DateTimeOffset.UtcNow,
                null);
            approvals.Add(approval);
            return ValueTask.FromResult(new CapabilityApprovalResult(approval, null, null));
        }

        public ValueTask<CapabilityApprovalResult> DecideAsync(
            CapabilityDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            DecisionCalls++;
            int index = approvals.FindIndex(item => item.Id == request.ApprovalId);
            CapabilityApprovalView decided = approvals[index] with
            {
                State = request.Decision is CapabilityDecision.Approve
                    ? CapabilityApprovalState.Approved
                    : CapabilityApprovalState.Denied,
                DecisionReason = request.Reason,
                DecidedAt = DateTimeOffset.UtcNow,
            };
            approvals[index] = decided;
            return ValueTask.FromResult(new CapabilityApprovalResult(decided, null, null));
        }

        public ValueTask<CapabilityApprovalSnapshot> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CapabilityApprovalSnapshot(
                approvals.Where(item => item.GoalId == goalId).ToArray(),
                null,
                null));
    }

    private sealed class FolderPicker(string folder) : IWorkspaceFolderPicker
    {
        internal TopLevel? Owner { get; private set; }

        public ValueTask<WorkspaceFolderPickerResult> PickAsync(
            TopLevel owner,
            WorkspaceFolderPath? currentFolder,
            CancellationToken cancellationToken = default)
        {
            Owner = owner;
            return ValueTask.FromResult(new WorkspaceFolderPickerResult(new(folder), null));
        }
    }
}
