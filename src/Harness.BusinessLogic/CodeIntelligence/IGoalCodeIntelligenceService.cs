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
}
