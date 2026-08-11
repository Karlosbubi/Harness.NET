namespace Harness.DataAccess.Inspection;

public sealed record WorkspaceInspectionPath(string Value);

public sealed record WorkspaceInspectionPattern(string Value);

public sealed record WorkspaceInspectionContinuation(string Value);

public enum WorkspaceTreeEntryKind
{
    Directory,
    File,
}

public sealed record WorkspaceTreeEntry(
    WorkspaceInspectionPath Path,
    WorkspaceTreeEntryKind Kind,
    int Depth);

public sealed record WorkspaceTreeQuery(
    WorkspaceInspectionPath Root,
    WorkspaceInspectionPattern? Glob,
    int MaximumDepth,
    int MaximumResults,
    WorkspaceInspectionContinuation? Continuation);

public sealed record WorkspaceTreeResult(
    IReadOnlyList<WorkspaceTreeEntry> Entries,
    WorkspaceInspectionContinuation? Continuation,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);

public sealed record WorkspaceRangeQuery(
    WorkspaceInspectionPath Path,
    int StartLine,
    int LineCount);

public sealed record WorkspaceRangeResult(
    WorkspaceInspectionPath Path,
    int StartLine,
    int EndLine,
    int TotalLines,
    string Content,
    string? Sha256,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);

public sealed record WorkspaceRegexQuery(
    WorkspaceInspectionPattern Pattern,
    WorkspaceInspectionPattern? FileGlob,
    int MaximumResults,
    WorkspaceInspectionContinuation? Continuation);

public sealed record WorkspaceRegexMatch(
    WorkspaceInspectionPath Path,
    int Line,
    int Character,
    int Length,
    string Text);

public sealed record WorkspaceRegexResult(
    IReadOnlyList<WorkspaceRegexMatch> Matches,
    int FilesScanned,
    WorkspaceInspectionContinuation? Continuation,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);

public interface IWorkspaceAdvancedInspector
{
    ValueTask<WorkspaceTreeResult> ListTreeAsync(
        string workspaceRoot,
        WorkspaceTreeQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<WorkspaceRangeResult> ReadRangeAsync(
        string workspaceRoot,
        WorkspaceRangeQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<WorkspaceRegexResult> SearchRegexAsync(
        string workspaceRoot,
        WorkspaceRegexQuery query,
        CancellationToken cancellationToken = default);
}
