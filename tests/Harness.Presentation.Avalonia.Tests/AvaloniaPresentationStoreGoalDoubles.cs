using System.Runtime.CompilerServices;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class AvaloniaPresentationStoreTests
{
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
            [new("tools")],
            Enum.GetValues<AgentRole>(),
            null,
            null,
            null,
            null);
        private static readonly GoalModelCandidate Remote = new(
            new("openrouter"),
            new("openai/gpt-5-mini"),
            ModelAccess.Remote,
            [new("tools")],
            Enum.GetValues<AgentRole>(),
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
            [new("tools")],
            Enum.GetValues<AgentRole>(),
            null,
            null,
            null,
            null);
        private readonly Dictionary<AgentRole, AgentRoleDefault> values =
            Enum.GetValues<AgentRole>().ToDictionary(role => role, role => new AgentRoleDefault(
                role,
                Local.Provider,
                Local.Model,
                Local.Access, AgentReasoningPolicy.Disabled,
                IsPersisted: false,
                UpdatedAt: null));

        public int DiscoveryCount { get; private set; }

        public ValueTask<AgentDefaultsSnapshot> GetAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Snapshot(models: []));

        public ValueTask<AgentDefaultsSnapshot> DiscoverAvailableAsync(
            CancellationToken cancellationToken = default)
        {
            DiscoveryCount++;
            return ValueTask.FromResult(Snapshot([Local]));
        }

        public ValueTask<AgentRoleDefaultUpdateResult> UpdateAsync(
            AgentRoleDefaultUpdate request,
            CancellationToken cancellationToken = default)
        {
            AgentRoleDefault value = new(
                request.Role,
                request.Provider,
                request.Model,
                Local.Access, request.ReasoningPolicy,
                IsPersisted: true,
                DateTimeOffset.UtcNow);
            values[request.Role] = value;
            return ValueTask.FromResult(new AgentRoleDefaultUpdateResult(value, null, null));
        }

        private AgentDefaultsSnapshot Snapshot(IReadOnlyList<GoalModelCandidate> models) =>
            new(
                values.Values.OrderBy(item => item.Role).ToArray(),
                models,
                [],
                [new(
                    Local.Provider,
                    Local.Access,
                    Local.Model,
                    models.Count,
                    models.Count(model => model.SupportedRoles.Count > 0),
                    HasPublishedPricing: false,
                    AgentModelProviderAvailability.Available,
                    Message: null)],
                []);
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

        internal GoalWorkflowRetryRole? RetriedRole { get; private set; }

        public ValueTask<GoalWorkflowSnapshot?> GetLatestAsync(
            GoalId goalId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(latest);

        public async IAsyncEnumerable<GoalWorkflowSnapshot> StartPlanningAsync(
            GoalWorkflowStartRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
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

        public ValueTask<GoalWorkflowSnapshot> AbortAsync(
            GoalWorkflowAbortRequest request,
            CancellationToken cancellationToken = default)
        {
            latest = Snapshot(request.GoalId, GoalWorkflowState.Aborted, "Goal aborted", false);
            return ValueTask.FromResult(latest);
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
            [new(1, GoalWorkflowCheckpointKind.Started, WorkflowActor.System, new(summary),
                DateTimeOffset.UtcNow)],
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

}
