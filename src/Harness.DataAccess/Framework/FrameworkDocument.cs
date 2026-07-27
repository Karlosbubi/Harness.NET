namespace Harness.DataAccess.Framework;

public sealed record FrameworkDocument(
    string Layer,
    int Precedence,
    string Source,
    string Content,
    bool IsPrivate);
