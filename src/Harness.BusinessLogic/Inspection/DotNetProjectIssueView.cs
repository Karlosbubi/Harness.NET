namespace Harness.BusinessLogic.Inspection;

public enum DotNetProjectIssueKindView
{
    Missing,
    OutsideWorkspace,
    TooLarge,
    InvalidMetadata,
}

public sealed record DotNetIssueProjectPathView(string Value);

public sealed record DotNetProjectIssueView(
    DotNetIssueProjectPathView Path,
    DotNetProjectIssueKindView Kind,
    string Message);
