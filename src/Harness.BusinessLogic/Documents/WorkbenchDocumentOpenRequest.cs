using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Documents;

public sealed record WorkbenchDocumentOpenRequest(
    WorkspaceId WorkspaceId,
    GoalId? GoalId,
    WorkbenchDocumentPath Path);
