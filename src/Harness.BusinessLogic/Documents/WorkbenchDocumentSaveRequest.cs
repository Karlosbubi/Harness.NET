using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Documents;

public sealed record WorkbenchDocumentSaveRequest(
    WorkspaceId WorkspaceId,
    GoalId? GoalId,
    ToolCorrelationId CorrelationId,
    WorkbenchDocumentPath Path,
    WorkbenchDocumentSha256? ExpectedSha256,
    WorkbenchDocumentContent Content);
