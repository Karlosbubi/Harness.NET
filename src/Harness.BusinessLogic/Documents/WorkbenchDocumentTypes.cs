namespace Harness.BusinessLogic.Documents;

public sealed record WorkbenchWorkspaceId(string Value);

public sealed record WorkbenchDocumentPath(string Value);

public sealed record WorkbenchDocumentContent(string Value);

public sealed record WorkbenchDocumentSha256(string Value);

public sealed record WorkbenchDocumentByteCount(long Value);

public sealed record WorkbenchBranchName(string Value);
