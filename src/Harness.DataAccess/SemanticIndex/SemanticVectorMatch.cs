namespace Harness.DataAccess.SemanticIndex;

public sealed record SemanticVectorMatch(
    string Id,
    string Path,
    int StartLine,
    int EndLine,
    string Content,
    string ContentHash,
    double Distance);
