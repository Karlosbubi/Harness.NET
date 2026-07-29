using Harness.BusinessLogic.Goals;

namespace Harness.BusinessLogic.Documents;

public sealed record WorkbenchDocumentView(
    WorkbenchWorkspaceId WorkspaceId,
    GoalId? GoalId,
    WorkbenchBranchName? Branch,
    WorkbenchDocumentPath Path,
    WorkbenchDocumentContent Content,
    WorkbenchDocumentSha256? Sha256,
    WorkbenchDocumentByteCount Size,
    bool IsTruncated,
    WorkbenchDocumentAccess Access,
    string AccessDescription,
    string? ErrorCode,
    string? Error);
