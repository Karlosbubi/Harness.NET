namespace Harness.BusinessLogic.Documents;

public sealed record WorkbenchDocumentPath(string Value);

public sealed record WorkbenchDocumentContent(string Value);

public sealed record WorkbenchDocumentSha256(string Value);

public sealed record WorkbenchDocumentByteCount(long Value);
