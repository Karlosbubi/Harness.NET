using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Documents;

public sealed record WorkbenchDocumentOpenRequest(
    WorkbenchWorkspaceId WorkspaceId,
    GoalId? GoalId,
    WorkbenchDocumentPath Path);
