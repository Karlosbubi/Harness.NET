namespace Harness.BusinessLogic.Inspection;

public sealed record GoalSourceContextId(string Value);

public sealed record GoalInspectionIdentity(
    GoalSourceContextId SourceContextId,
    string WorkspaceId,
    string GoalId,
    GoalWorkspaceScope Scope,
    string Branch,
    string EntryPoint);

public sealed record GoalTreeEntryView(
    string Path,
    string Kind,
    int Depth);

public sealed record GoalTreeView(
    GoalInspectionIdentity? Identity,
    IReadOnlyList<GoalTreeEntryView> Entries,
    string? Continuation,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);

public sealed record GoalFileRangeView(
    GoalInspectionIdentity? Identity,
    string Path,
    int StartLine,
    int EndLine,
    int TotalLines,
    string Content,
    string? Sha256,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);

public sealed record GoalRegexMatchView(
    string Path,
    int Line,
    int Character,
    int Length,
    string Text);

public sealed record GoalRegexSearchView(
    GoalInspectionIdentity? Identity,
    IReadOnlyList<GoalRegexMatchView> Matches,
    int FilesScanned,
    string? Continuation,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);

public sealed record GoalProjectDependencyView(
    string Project,
    string Dependency);

public sealed record GoalProjectGraphView(
    GoalInspectionIdentity? Identity,
    IReadOnlyList<DotNetProjectView> Projects,
    IReadOnlyList<GoalProjectDependencyView> ProjectDependencies,
    bool IsTruncated,
    string? ErrorCode,
    string? Error);
