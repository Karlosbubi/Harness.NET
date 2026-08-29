using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Harness.Presentation.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed partial class AvaloniaPresentationStoreTests
{
    /// <summary>Builds a store over the deterministic fakes so other suites can drive real dialogs.</summary>
    internal static AvaloniaPresentationStore CreateStore(
        IModelProviderSettingsService? providerSettingsService = null,
        IMcpSettingsService? mcpSettingsService = null,
      IVisualCaptureService? visualCaptureService = null,
        IResearchSettingsService? researchSettingsService = null,
        IDocumentationResearchService? documentationResearchService = null,
        IDependencyResearchService? dependencyResearchService = null,
        IEditorIntelligenceSettingsService? editorIntelligenceSettingsService = null,
        IKeybindingSettingsService? keybindingSettingsService = null) => new(
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
        NullLogger<AvaloniaPresentationStore>.Instance,
        providerSettingsService,
        remoteSpendPreferenceService: null,
        mcpSettingsService,
      visualCaptureService,
      researchSettingsService,
      documentationResearchService,
      dependencyResearchService,
      editorIntelligenceSettingsService: editorIntelligenceSettingsService,
      keybindingSettingsService: keybindingSettingsService);

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

    private sealed class EditorIntelligenceSettingsService :
        IEditorIntelligenceSettingsService
    {
        internal EditorIntelligencePreferences Initial { get; } = new(
            true, true, true, true, true, true, true);
        internal int SaveCalls { get; private set; }

        public ValueTask<EditorIntelligenceSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            new EditorIntelligenceSettingsSnapshot(
            Initial,
            "Roslyn editor adornments are available for trusted C# source buffers."));

        public ValueTask<EditorIntelligenceSettingsSnapshot> SaveAsync(
            EditorIntelligencePreferences preferences,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return ValueTask.FromResult(new EditorIntelligenceSettingsSnapshot(
                preferences,
                "Roslyn editor adornments are available for trusted C# source buffers."));
        }
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

        public ValueTask<WorkspaceResult> RefreshAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkspaceResult(workspace, [],
                workspace?.Id == workspaceId ? null : "Workspace missing."));
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
        public ValueTask<WorkspaceResult> RefreshAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkspaceResult(
                workspaces.SingleOrDefault(item => item.Id == workspaceId), [], null));
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

    private sealed class ApplicationOperationsService : IApplicationOperationsService
    {
        internal BackupDestinationPath? LastDestination { get; private set; }
        internal RestoreSourcePath? LastRestoreSource { get; private set; }

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

        public ValueTask<ApplicationRestoreInspectionResult> InspectRestoreAsync(
            RestoreSourcePath source,
            CancellationToken cancellationToken = default)
        {
            LastRestoreSource = source;
            return ValueTask.FromResult(new ApplicationRestoreInspectionResult(
                Restore(source), null, null));
        }

        public ValueTask<ApplicationRestoreStageResult> StageRestoreAsync(
            ApplicationRestoreStageRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRestoreSource = request.Source;
            return ValueTask.FromResult(new ApplicationRestoreStageResult(
                Restore(request.Source), true, null, null));
        }

        private static ApplicationRestoreView Restore(RestoreSourcePath source) => new(
            source, new(new string('a', 64)), new(new string('b', 64)), new(4096),
            null, null, new(21), DateTimeOffset.UtcNow, RestoreArchiveFormat.Version2);
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
