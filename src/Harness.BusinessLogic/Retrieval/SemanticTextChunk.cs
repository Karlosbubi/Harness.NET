namespace Harness.BusinessLogic.Retrieval;

internal sealed record SemanticTextChunk(
    string Id,
    string Path,
    int StartLine,
    int EndLine,
    string Content,
    string ContentHash);
