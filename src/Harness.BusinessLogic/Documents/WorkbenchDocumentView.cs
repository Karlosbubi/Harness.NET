using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Documents;

public sealed record WorkbenchDocumentView(
    WorkspaceId WorkspaceId,
    GoalId? GoalId,
    WorkspaceBranchName? Branch,
    WorkbenchDocumentPath Path,
    WorkbenchDocumentContent Content,
    WorkbenchDocumentSha256? Sha256,
    WorkbenchDocumentByteCount Size,
    bool IsTruncated,
    WorkbenchDocumentAccess Access,
    string AccessDescription,
    string? ErrorCode,
    string? Error);
