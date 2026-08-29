namespace Harness.DataAccess.Inspection;

public enum DotNetProjectIssueKind
{
    Missing,
    OutsideWorkspace,
    TooLarge,
    InvalidMetadata,
}

public sealed record DotNetIssueProjectPath(string Value);

public sealed record DotNetProjectIssue(
    DotNetIssueProjectPath Path,
    DotNetProjectIssueKind Kind,
    string Message);
