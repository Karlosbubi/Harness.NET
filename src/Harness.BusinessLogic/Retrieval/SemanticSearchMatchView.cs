namespace Harness.BusinessLogic.Retrieval;

public sealed record SemanticSearchMatchView(
    string Path,
    int StartLine,
    int EndLine,
    string Content,
    VectorDistance Distance);
