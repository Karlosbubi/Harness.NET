using Harness.BusinessLogic.Documents;

namespace Harness.BusinessLogic.Inspection;

public sealed record WorkspaceFileCatalogView(
    IReadOnlyList<WorkbenchDocumentPath> Files,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
