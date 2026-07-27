namespace Harness.DataAccess.SemanticIndex;

public sealed record TrackedTextDocument(
    string Path,
    string Content,
    string ContentHash);
