using Harness.BusinessLogic.Inspection;

namespace Harness.BusinessLogic.CodeIntelligence;

public sealed record GoalCodeResultIdentity(
    string WorkspaceId,
    string GoalId,
    string SourceContextId,
    string Scope,
    string? Project,
    IReadOnlyList<string> TargetFrameworks,
    string Configuration,
    string? DocumentVersion,
    DateTimeOffset FreshAt);

public sealed record GoalCodeProblemsView(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeDiagnostic> Diagnostics,
    WorkbenchCodeIssue? Issue,
    GoalCodeResultIdentity? Identity = null);

public sealed record GoalCodeSymbolView(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodePosition Position,
    WorkbenchCodeResultState State,
    WorkbenchCodeRange? ApplicableRange,
    IReadOnlyList<WorkbenchCodeMessage> Sections,
    WorkbenchCodeIssue? Issue,
    GoalCodeResultIdentity? Identity = null);

public sealed record GoalCodeNavigationView(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodePosition Position,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeSymbolDestination> Destinations,
    WorkbenchCodeIssue? Issue,
    GoalCodeResultIdentity? Identity = null,
    IReadOnlyList<WorkbenchCodeVirtualDocumentView>? VirtualDocuments = null);

public sealed record GoalMissingImportView(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodePosition Position,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeMissingImportCandidate> Candidates,
    WorkbenchCodeIssue? Issue,
    GoalCodeResultIdentity? Identity = null);

public sealed record GoalCodeActionView(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodePosition Position,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeActionCandidate> Candidates,
    WorkbenchCodeIssue? Issue,
    GoalCodeResultIdentity? Identity = null);

public sealed record GoalCodeSemanticView(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodePosition Position,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeSemanticItem> Items,
    int? Continuation,
    bool IsTruncated,
    WorkbenchCodeIssue? Issue,
    GoalCodeResultIdentity? Identity = null);

public sealed record GoalProjectProblemsView(
    GoalInspectionIdentity? Identity,
    int FilesChecked,
    IReadOnlyList<WorkbenchCodeDiagnostic> Diagnostics,
    bool IsTruncated,
    string? Continuation,
    IReadOnlyList<WorkbenchCodeIssue> Issues,
    DateTimeOffset FreshAt);
