using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Documents;

public sealed record WorkbenchDocumentSaveResult(
    WorkspaceId WorkspaceId,
    GoalId? GoalId,
    ToolCorrelationId CorrelationId,
    WorkbenchDocumentPath Path,
    WorkbenchDocumentSha256? ExpectedSha256,
    WorkbenchDocumentSha256? CurrentSha256,
    WorkbenchDocumentSha256? SavedSha256,
    WorkbenchDocumentByteCount BytesWritten,
    WorkbenchDocumentSaveOutcome Outcome,
    string? ErrorCode,
    string? Error);
