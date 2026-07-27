namespace Harness.DataAccess.SemanticIndex;

public sealed record SemanticChunkVector(
    string Id,
    string Path,
    int StartLine,
    int EndLine,
    string Content,
    string ContentHash,
    IReadOnlyList<float> Vector);
