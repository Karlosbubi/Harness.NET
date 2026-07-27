namespace Harness.DataAccess.Inspection;

public sealed record WorkspaceTextMatch(
    string Path,
    int LineNumber,
    string Text);
