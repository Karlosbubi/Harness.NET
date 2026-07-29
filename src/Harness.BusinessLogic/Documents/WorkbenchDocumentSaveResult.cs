using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Documents;

public sealed record WorkbenchDocumentSaveResult(
    GoalId GoalId,
    ToolCorrelationId CorrelationId,
    WorkbenchDocumentPath Path,
    WorkbenchDocumentSha256? ExpectedSha256,
    WorkbenchDocumentSha256? CurrentSha256,
    WorkbenchDocumentSha256? SavedSha256,
    WorkbenchDocumentByteCount BytesWritten,
    WorkbenchDocumentSaveOutcome Outcome,
    string? ErrorCode,
    string? Error);
