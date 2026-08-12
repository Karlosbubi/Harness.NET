using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;

namespace Harness.BusinessLogic.CodeIntelligence;

internal interface IGoalCodeIntelligenceService
{
    ValueTask<GoalCodeProblemsView> InspectProblemsAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        CancellationToken cancellationToken = default);

    ValueTask<GoalCodeSymbolView> GetSymbolAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken = default);

    ValueTask<GoalCodeNavigationView> FindDefinitionAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken = default);

    ValueTask<GoalCodeNavigationView> FindReferencesAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken = default);

    ValueTask<GoalCodeNavigationView> FindImplementationsAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken = default);

    ValueTask<GoalMissingImportView> FindMissingImportsAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    ValueTask<GoalCodeActionView> FindCodeActionsAsync(
        GoalId goalId,
        GoalWorkspaceScope scope,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position,
        WorkbenchCodeRange? range = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    ValueTask<GoalCodeSemanticView> SearchSymbolsAsync(
        GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
        string query, int maximumResults, int offset,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    ValueTask<GoalCodeSemanticView> AnalyzeCallsAsync(
        GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position, int maximumResults, int offset,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    ValueTask<GoalCodeSemanticView> GetTypeHierarchyAsync(
        GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position, int maximumResults, int offset,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    ValueTask<GoalCodeSemanticView> FindAssociatedTestsAsync(
        GoalId goalId, GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
        WorkbenchCodePosition position, int maximumResults, int offset,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    ValueTask<GoalProjectProblemsView> InspectProjectProblemsAsync(
        GoalId goalId, GoalWorkspaceScope scope, int maximumFiles,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
