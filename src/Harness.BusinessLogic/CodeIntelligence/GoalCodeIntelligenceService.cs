using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.CodeIntelligence;

internal sealed class GoalCodeIntelligenceService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore,
    IGoalWorkspaceInspectionService inspectionService,
    IWorkbenchCodeIntelligenceService codeIntelligenceService) : IGoalCodeIntelligenceService
{
    private static readonly WorkbenchCodeBufferVersion InitialBufferVersion = new(1);

    public async ValueTask<GoalCodeProblemsView> InspectProblemsAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        CancellationToken cancellationToken = default)
    {
        PreparedQuery prepared = await PrepareAsync(
            goalId, scope, path, position: null, cancellationToken);
        if (prepared.Issue is not null)
        {
            return new(path, prepared.State, [], prepared.Issue);
        }

        try
        {
            WorkbenchCodeDiagnosticView result = await codeIntelligenceService.SynchronizeAsync(
                prepared.Document!, cancellationToken);
            return new(path, result.State, result.Diagnostics, result.Issues.FirstOrDefault());
        }
        finally
        {
            await codeIntelligenceService.StopAsync(prepared.SessionId!, CancellationToken.None);
        }
    }

    public ValueTask<GoalCodeSymbolView> GetSymbolAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken = default) =>
        SymbolAsync(goalId, scope, path, position, cancellationToken);

    public ValueTask<GoalCodeNavigationView> FindDefinitionAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken = default) =>
        NavigateAsync(goalId, scope, path, position, references: false, cancellationToken);

    public ValueTask<GoalCodeNavigationView> FindReferencesAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken = default) =>
        NavigateAsync(goalId, scope, path, position, references: true, cancellationToken);

    private async ValueTask<GoalCodeSymbolView> SymbolAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken)
    {
        PreparedQuery prepared = await PrepareAsync(
            goalId, scope, path, position, cancellationToken);
        if (prepared.Issue is not null)
        {
            return new(path, position, prepared.State, null, [], prepared.Issue);
        }

        try
        {
            WorkbenchCodeQuickInfoView result = await codeIntelligenceService.GetQuickInfoAsync(
                prepared.Interactive!, cancellationToken);
            return new(path, position, result.State, result.ApplicableRange, result.Sections,
                result.Issues.FirstOrDefault());
        }
        finally
        {
            await codeIntelligenceService.StopAsync(prepared.SessionId!, CancellationToken.None);
        }
    }

    private async ValueTask<GoalCodeNavigationView> NavigateAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        bool references,
        CancellationToken cancellationToken)
    {
        PreparedQuery prepared = await PrepareAsync(
            goalId, scope, path, position, cancellationToken);
        if (prepared.Issue is not null)
        {
            return new(path, position, prepared.State, [], prepared.Issue);
        }

        try
        {
            WorkbenchCodeNavigationView result = references
                ? await codeIntelligenceService.FindReferencesAsync(
                    prepared.Interactive!, cancellationToken)
                : await codeIntelligenceService.FindDefinitionAsync(
                    prepared.Interactive!, cancellationToken);
            return new(path, position, result.State, result.Destinations,
                result.Issues.FirstOrDefault());
        }
        finally
        {
            await codeIntelligenceService.StopAsync(prepared.SessionId!, CancellationToken.None);
        }
    }

    private async ValueTask<PreparedQuery> PrepareAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition? position,
        CancellationToken cancellationToken)
    {
        if (goalId is null || string.IsNullOrWhiteSpace(goalId.Value) ||
            path is null || string.IsNullOrWhiteSpace(path.Value) || !Enum.IsDefined(scope) ||
            position is { Line: < 0 } or { Character: < 0 })
        {
            return PreparedQuery.Failure("invalid_semantic_query",
                "A goal, source path, and non-negative zero-based position are required.");
        }

        StoredGoal? goal = await goalStore.GetAsync(goalId.Value, cancellationToken);
        RegisteredWorkspace? workspace = await workspaceStore.GetActiveAsync(cancellationToken);
        if (goal is null || workspace is null || !workspace.IsTrusted ||
            !workspace.Id.Equals(goal.WorkspaceId, StringComparison.Ordinal))
        {
            return PreparedQuery.Failure("goal_workspace_unavailable",
                "The trusted goal workspace is unavailable.");
        }

        WorkspaceFileView file = await inspectionService.ReadFileAsync(
            goalId, scope, path.Value, cancellationToken);
        if (file.ErrorCode is not null || file.Sha256 is null || file.IsTruncated)
        {
            return PreparedQuery.Failure(file.ErrorCode ?? "source_file_too_large",
                file.Error ?? "The complete source file is required for semantic analysis.");
        }

        string entryPoint = Path.GetRelativePath(workspace.RootPath, workspace.EntryPoint);
        WorkbenchCodeSessionView session = await codeIntelligenceService.StartAsync(
            new(
                new(workspace.Id),
                scope is GoalWorkspaceScope.ApprovedWorktree ? goalId : null,
                new(entryPoint)),
            progress: null,
            cancellationToken);
        if (session.SessionId is null || session.State is WorkbenchCodeResultState.Failed or
            WorkbenchCodeResultState.Cancelled or WorkbenchCodeResultState.Stale)
        {
            return PreparedQuery.Failure("code_intelligence_unavailable",
                session.Issues.FirstOrDefault()?.Message.Value ??
                "Roslyn code intelligence is unavailable.", session.State);
        }

        WorkbenchCodeDocumentSnapshot document = new(
            session.SessionId,
            path,
            new(file.Sha256),
            InitialBufferVersion,
            new(file.Content));
        WorkbenchCodeInteractiveSnapshot? interactive = position is null
            ? null
            : new(
                session.SessionId,
                path,
                new(file.Sha256),
                InitialBufferVersion,
                new(file.Content),
                position);
        return new(session.SessionId, document, interactive, session.State, null);
    }

    private sealed record PreparedQuery(
        WorkbenchCodeSessionId? SessionId,
        WorkbenchCodeDocumentSnapshot? Document,
        WorkbenchCodeInteractiveSnapshot? Interactive,
        WorkbenchCodeResultState State,
        WorkbenchCodeIssue? Issue)
    {
        internal static PreparedQuery Failure(
            string code,
            string message,
            WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) =>
            new(null, null, null, state, new(new(code), new(message)));
    }
}
