using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Coverage;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Privacy;
using Harness.BusinessLogic.ProjectSecrets;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Agents;
using Harness.DataAccess.Approvals;
using Harness.DataAccess.CodeIntelligence;
using Harness.DataAccess.Commits;
using Harness.DataAccess.Conversations;
using Harness.DataAccess.Coverage;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Framework;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Inspection;
using Harness.DataAccess.Layouts;
using Harness.DataAccess.Models;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.ProjectSecrets;
using Harness.DataAccess.SemanticIndex;
using Harness.DataAccess.VisualCapture;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;
using Harness.DataAccess.Workflows;
using Harness.Host.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Harness.Host;

internal static class WorkspaceServiceRegistration
{
    internal static IServiceCollection AddHarnessWorkspace(
        this IServiceCollection services,
        HarnessConfiguration configuration)
    {
        services.AddSingleton<IConversationStore, SqliteConversationStore>();
        services.AddSingleton<IFrameworkSourceReader, FileFrameworkSourceReader>();
        services.AddSingleton<IFrameworkOverlayStore, SqliteFrameworkOverlayStore>();
        services.AddSingleton<IWorkspaceInspector, GitWorkspaceInspector>();
        services.AddSingleton<IWorkspaceStore, SqliteWorkspaceStore>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IGoalStore, SqliteGoalStore>();
        services.AddSingleton<IGoalModelSelectionStore, SqliteGoalModelSelectionStore>();
        services.AddSingleton<IAgentRoleDefaultStore, SqliteAgentRoleDefaultStore>();
        services.AddSingleton<IAgentToolExposureConfigurationStore,
            XdgAgentToolExposureConfigurationStore>();
        services.AddSingleton<IAgentToolExposureSettingsService,
            AgentToolExposureSettingsService>();
        services.AddSingleton<IRemoteCostStore, SqliteRemoteCostStore>();
        services.AddSingleton<IRemoteCostService, RemoteCostService>();
        services.AddSingleton<ICapabilityApprovalStore, SqliteCapabilityApprovalStore>();
        services.AddSingleton<ICapabilityApprovalService, CapabilityApprovalService>();
        services.AddSingleton<IToolEvidenceStore, SqliteToolEvidenceStore>();
        services.AddSingleton<IToolEvidenceService, ToolEvidenceService>();
        services.AddSingleton<IAgentToolActivationService, AgentToolActivationService>();
        services.AddSingleton<ISensitiveDisplayGuard, SensitiveDisplayGuard>();
        services.AddSingleton<IVisualCapturePreferenceStore, SqliteVisualCapturePreferenceStore>();
        services.AddSingleton<IVisualCapturePortal, XdgDesktopPortalVisualCapture>();
        services.AddSingleton<IVisualCaptureImageSourceReader, PortalFileImageSourceReader>();
        services.AddSingleton<IVisualCaptureArtifactStore, FileVisualCaptureArtifactStore>();
        services.AddSingleton<IVisualCaptureService, VisualCaptureService>();
        services.AddSingleton<IRunOutputService, RunOutputService>();
        services.AddSingleton<IDotNetProjectRunner, DotNetProjectRunner>();
        services.AddSingleton<IDeveloperDotNetExecutionStore, SqliteDeveloperDotNetExecutionStore>();
        services.AddSingleton<IDeveloperProjectExecutionService, DeveloperProjectExecutionService>();
        services.AddSingleton<IWorkspaceCoverageReader, CoberturaWorkspaceCoverageReader>();
        services.AddSingleton<IDeveloperCoverageStore, SqliteDeveloperCoverageStore>();
        services.AddSingleton<IDeveloperCoverageService, DeveloperCoverageService>();
        services.AddSingleton<IGoalService, GoalService>();
        services.AddSingleton<IGoalWorktreeManager, GitGoalWorktreeManager>();
        services.AddSingleton<IWorkspaceFileEditor, AtomicWorkspaceFileEditor>();
        services.AddSingleton<IDotNetToolRunner, DotNetToolRunner>();
        services.AddSingleton<IWorkspaceMutationService, WorkspaceMutationService>();
        services.AddSingleton<IWorkspaceFileReader, WorkspaceFileReader>();
        services.AddSingleton<IWorkspaceFileCatalogReader, GitWorkspaceFileCatalogReader>();
        services.AddSingleton<IWorkspaceTextSearcher, GitWorkspaceTextSearcher>();
        services.AddSingleton<IWorkspaceAdvancedInspector, GitWorkspaceAdvancedInspector>();
        services.AddSingleton<IWorkspaceGitInspector, LibGitWorkspaceGitInspector>();
        services.AddSingleton<IDeveloperGitRepository, LibGitDeveloperGitRepository>();
        services.AddSingleton<IWorkspaceDotNetInspector, WorkspaceDotNetInspector>();
        services.AddSingleton<IProjectUserSecretsPathResolver,
            PlatformProjectUserSecretsPathResolver>();
        services.AddSingleton<IProjectUserSecretStore, ProjectUserSecretStore>();
        services.AddSingleton<IProjectUserSecretsService, ProjectUserSecretsService>();
        services.AddSingleton<IDotNetProcess, DotNetProcess>();
        services.AddSingleton<DotNetSdkSelector>();
        services.AddSingleton<IMSBuildRuntime, MSBuildRuntime>();
        services.AddSingleton<IRoslynWorkspaceProbe, RoslynWorkspaceProbe>();
        services.AddSingleton<ICodeIntelligenceEngine, RoslynCodeIntelligenceEngine>();
        services.AddSingleton<IWorkspaceInspectionService, WorkspaceInspectionService>();
        services.AddSingleton<IWorkbenchWorkspaceContextResolver, WorkbenchWorkspaceContextResolver>();
        services.AddSingleton<IWorkbenchCodeIntelligenceService, WorkbenchCodeIntelligenceService>();
        services.AddSingleton<IKeybindingSettingsService, KeybindingSettingsService>();
        services.AddSingleton<IWorkbenchInspectionService, WorkbenchInspectionService>();
        services.AddSingleton<IDeveloperGitService, DeveloperGitService>();
        services.AddSingleton<IWorkbenchDocumentService, WorkbenchDocumentService>();
        services.AddSingleton<IWorkbenchLayoutStore, FileWorkbenchLayoutStore>();
        services.AddSingleton<IWorkbenchLayoutService, WorkbenchLayoutService>();
        services.AddSingleton<IGoalWorkspaceInspectionService, GoalWorkspaceInspectionService>();
        services.AddSingleton<IGoalCodeIntelligenceService, GoalCodeIntelligenceService>();
        services.AddSingleton<IChangedSetQualityService, ChangedSetQualityService>();
        services.AddSingleton<ITrackedTextCatalogReader, GitTrackedTextCatalogReader>();
        services.AddSingleton<ISemanticIndexStore, SqliteSemanticIndexStore>();
        services.AddSingleton<IFrameworkResolver, FrameworkResolver>();
        services.AddSingleton(new FrameworkOptions(configuration.Framework.Rules
            .Select(rule => new FrameworkRule(
                rule.Key,
                rule.Value,
                rule.Precedence,
                rule.Layer,
                rule.IsLocked,
                rule.Source))
            .ToArray()));
        services.AddSingleton<IFrameworkService, FrameworkService>();
        services.AddSingleton<IGoalWorkflowStore, SqliteGoalWorkflowStore>();
        services.AddSingleton<IGoalWorkflowTaskStore, SqliteGoalWorkflowTaskStore>();
        services.AddSingleton<IGoalCommitApprovalStore, SqliteGoalCommitApprovalStore>();
        services.AddSingleton<IGoalCommitter, LibGitGoalCommitter>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
