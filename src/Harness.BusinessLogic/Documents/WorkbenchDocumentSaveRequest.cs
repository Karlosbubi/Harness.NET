using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Documents;

public sealed record WorkbenchDocumentSaveRequest(
    GoalId GoalId,
    ToolCorrelationId CorrelationId,
    WorkbenchDocumentPath Path,
    WorkbenchDocumentSha256? ExpectedSha256,
    WorkbenchDocumentContent Content);
