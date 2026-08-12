using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Workspaces;

namespace Harness.BusinessLogic.CodeIntelligence;

internal sealed class GoalCodeIntelligenceService(
    IGoalStore goalStore,
    IWorkspaceStore workspaceStore,
    IGoalWorkspaceInspectionService inspectionService,
    IWorkbenchCodeIntelligenceService codeIntelligenceService,
    TimeProvider? timeProvider = null) : IGoalCodeIntelligenceService
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
            return new(path, prepared.State, [], prepared.Issue, prepared.Identity);
        }

        try
        {
            WorkbenchCodeDiagnosticView result = await codeIntelligenceService.SynchronizeAsync(
                prepared.Document!, cancellationToken);
            return new(path, result.State, result.Diagnostics, result.Issues.FirstOrDefault(),
                prepared.Identity);
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
        NavigateAsync(goalId, scope, path, position, NavigationKind.Definition,
            cancellationToken);

    public ValueTask<GoalCodeNavigationView> FindReferencesAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken = default) =>
        NavigateAsync(goalId, scope, path, position, NavigationKind.References,
            cancellationToken);

    public ValueTask<GoalCodeNavigationView> FindImplementationsAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken = default) =>
        NavigateAsync(goalId, scope, path, position, NavigationKind.Implementations,
            cancellationToken);

    public async ValueTask<GoalMissingImportView> FindMissingImportsAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken = default)
    {
        PreparedQuery prepared = await PrepareAsync(
            goalId, scope, path, position, cancellationToken);
        if (prepared.Issue is not null)
        {
            return new(path, position, prepared.State, [], prepared.Issue, prepared.Identity);
        }

        try
        {
            WorkbenchCodeMissingImportView result =
                await codeIntelligenceService.GetMissingImportsAsync(
                    prepared.Interactive!, cancellationToken);
            return new(path, position, result.State, result.Candidates,
                result.Issues.FirstOrDefault(), prepared.Identity);
        }
        finally
        {
            await codeIntelligenceService.StopAsync(prepared.SessionId!, CancellationToken.None);
        }
    }

    public ValueTask<GoalCodeSemanticView> SearchSymbolsAsync(
        GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
        string query, int maximumResults, int offset, CancellationToken cancellationToken = default) =>
        SemanticAsync(goalId, scope, path, new(0, 0), query, maximumResults, offset,
            SemanticKind.Symbols, cancellationToken);
    public ValueTask<GoalCodeSemanticView> AnalyzeCallsAsync(
        GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position, int maximumResults, int offset,
        CancellationToken cancellationToken = default) => SemanticAsync(goalId, scope, path,
            position, null, maximumResults, offset, SemanticKind.Calls, cancellationToken);
    public ValueTask<GoalCodeSemanticView> GetTypeHierarchyAsync(
        GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position, int maximumResults, int offset,
        CancellationToken cancellationToken = default) => SemanticAsync(goalId, scope, path,
            position, null, maximumResults, offset, SemanticKind.Types, cancellationToken);
    public ValueTask<GoalCodeSemanticView> FindAssociatedTestsAsync(
        GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position, int maximumResults, int offset,
        CancellationToken cancellationToken = default) => SemanticAsync(goalId, scope, path,
            position, null, maximumResults, offset, SemanticKind.Tests, cancellationToken);

    public async ValueTask<GoalProjectProblemsView> InspectProjectProblemsAsync(
        GoalId goalId, GoalWorkspaceScope scope, int maximumFiles,
        CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(maximumFiles, 1, 100);
        GoalTreeView tree = await inspectionService.ListTreeAsync(goalId, scope, string.Empty,
            "*.cs", 32, limit, null, cancellationToken);
        List<WorkbenchCodeDiagnostic> diagnostics = [];
        List<WorkbenchCodeIssue> issues = [];
        foreach (GoalTreeEntryView entry in tree.Entries.Where(item =>
                     item.Kind.Equals("File", StringComparison.OrdinalIgnoreCase)))
        {
            GoalCodeProblemsView result = await InspectProblemsAsync(
                goalId, scope, new(entry.Path), cancellationToken);
            diagnostics.AddRange(result.Diagnostics);
            if (result.Issue is not null) issues.Add(result.Issue);
        }
        return new(tree.Identity, tree.Entries.Count, diagnostics.Take(5_000).ToArray(),
            tree.IsTruncated || diagnostics.Count > 5_000, tree.Continuation, issues,
            (timeProvider ?? TimeProvider.System).GetUtcNow());
    }

    private async ValueTask<GoalCodeSemanticView> SemanticAsync(
        GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position, string? query, int maximumResults, int offset,
        SemanticKind kind, CancellationToken cancellationToken)
    {
        PreparedQuery prepared = await PrepareAsync(goalId, scope, path, position, cancellationToken);
        if (prepared.Issue is not null)
            return new(path, position, prepared.State, [], null, false, prepared.Issue,
                prepared.Identity);
        try
        {
            WorkbenchCodeSemanticQuery request = new(prepared.Interactive!, query, maximumResults, offset);
            WorkbenchCodeSemanticView result = kind switch
            {
                SemanticKind.Symbols => await codeIntelligenceService.SearchSymbolsAsync(request, cancellationToken),
                SemanticKind.Calls => await codeIntelligenceService.AnalyzeCallsAsync(request, cancellationToken),
                SemanticKind.Types => await codeIntelligenceService.GetTypeHierarchyAsync(request, cancellationToken),
                SemanticKind.Tests => await codeIntelligenceService.FindAssociatedTestsAsync(request, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            return new(path, position, result.State, result.Items, result.Continuation,
                result.IsTruncated, result.Issues.FirstOrDefault(), prepared.Identity);
        }
        finally { await codeIntelligenceService.StopAsync(prepared.SessionId!, CancellationToken.None); }
    }

    private enum SemanticKind { Symbols, Calls, Types, Tests }

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
            return new(path, position, prepared.State, null, [], prepared.Issue,
                prepared.Identity);
        }

        try
        {
            WorkbenchCodeQuickInfoView result = await codeIntelligenceService.GetQuickInfoAsync(
                prepared.Interactive!, cancellationToken);
            return new(path, position, result.State, result.ApplicableRange, result.Sections,
                result.Issues.FirstOrDefault(), prepared.Identity);
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
        NavigationKind kind,
        CancellationToken cancellationToken)
    {
        PreparedQuery prepared = await PrepareAsync(
            goalId, scope, path, position, cancellationToken);
        if (prepared.Issue is not null)
        {
            return new(path, position, prepared.State, [], prepared.Issue, prepared.Identity);
        }

        try
        {
            WorkbenchCodeNavigationView result = kind switch
            {
                NavigationKind.Definition =>
                    await codeIntelligenceService.FindDefinitionAsync(
                        prepared.Interactive!, cancellationToken),
                NavigationKind.References =>
                    await codeIntelligenceService.FindReferencesAsync(
                        prepared.Interactive!, cancellationToken),
                NavigationKind.Implementations =>
                    await codeIntelligenceService.FindImplementationsAsync(
                        prepared.Interactive!, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            return new(path, position, result.State, result.Destinations,
                result.Issues.FirstOrDefault(), prepared.Identity);
        }
        finally
        {
            await codeIntelligenceService.StopAsync(prepared.SessionId!, CancellationToken.None);
        }
    }

    private enum NavigationKind
    {
        Definition,
        References,
        Implementations,
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

        GoalProjectGraphView graph;
        try
        {
            graph = await inspectionService.InspectProjectGraphAsync(
                goalId, scope, cancellationToken);
        }
        catch (NotSupportedException)
        {
            graph = new(null, [], [], false, null, null);
        }
        DotNetProjectView? project = graph.Projects
            .Where(candidate => ProjectContains(candidate.Path, path.Value))
            .OrderByDescending(candidate => candidate.Path.Length)
            .FirstOrDefault();
        GoalCodeResultIdentity? identity = graph.Identity is null ? null : new(
            graph.Identity.WorkspaceId, graph.Identity.GoalId,
            graph.Identity.SourceContextId.Value, graph.Identity.Scope.ToString(), project?.Path,
            project?.TargetFrameworks ?? [], "Debug", file.Sha256,
            (timeProvider ?? TimeProvider.System).GetUtcNow());

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
                "Roslyn code intelligence is unavailable.", session.State, identity);
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
        return new(session.SessionId, document, interactive, session.State, null, identity);
    }

    private static bool ProjectContains(string projectPath, string documentPath)
    {
        string directory = (Path.GetDirectoryName(projectPath) ?? string.Empty)
            .Replace(Path.DirectorySeparatorChar, '/');
        return directory.Length == 0 || documentPath.StartsWith(directory + "/",
            StringComparison.Ordinal);
    }

    private sealed record PreparedQuery(
        WorkbenchCodeSessionId? SessionId,
        WorkbenchCodeDocumentSnapshot? Document,
        WorkbenchCodeInteractiveSnapshot? Interactive,
        WorkbenchCodeResultState State,
        WorkbenchCodeIssue? Issue,
        GoalCodeResultIdentity? Identity)
    {
        internal static PreparedQuery Failure(
            string code,
            string message,
            WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed,
            GoalCodeResultIdentity? identity = null) =>
            new(null, null, null, state, new(new(code), new(message)), identity);
    }
}
