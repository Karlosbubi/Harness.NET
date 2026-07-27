namespace Harness.BusinessLogic.Framework;

public sealed record FrameworkDocumentView(
    string Layer,
    int Precedence,
    string Source,
    string Content,
    bool IsPrivate);
