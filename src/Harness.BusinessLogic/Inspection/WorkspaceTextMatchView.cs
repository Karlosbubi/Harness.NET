namespace Harness.BusinessLogic.Inspection;

public sealed record WorkspaceTextMatchView(
    string Path,
    int LineNumber,
    string Text);
