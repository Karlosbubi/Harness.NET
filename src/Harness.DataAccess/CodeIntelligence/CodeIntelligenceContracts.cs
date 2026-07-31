namespace Harness.DataAccess.CodeIntelligence;

public sealed record CodeIntelligenceContextId(string Value);

public sealed record CodeIntelligenceSessionId(string Value);

public sealed record CodeIntelligenceRootPath(string Value);

public sealed record CodeIntelligenceEntryPoint(string Value);

public sealed record CodeIntelligenceDocumentPath(string Value);

public sealed record CodeIntelligenceBaselineHash(string Value);

public sealed record CodeIntelligenceBufferVersion(long Value);

public sealed record CodeIntelligenceText(string Value);

public sealed record CodeIntelligenceIssueCode(string Value);

public sealed record CodeIntelligenceMessage(string Value);

public sealed record CodeIntelligenceDiagnosticId(string Value);

public sealed record CodeIntelligenceDiagnosticSource(string Value);

public sealed record CodeIntelligenceProjectName(string Value);

public enum CodeIntelligenceSourceKind
{
    OriginalWorkspace,
    ApprovedGoalWorktree,
}

public enum CodeIntelligenceResultState
{
    Ready,
    Loading,
    Degraded,
    Cancelled,
    Failed,
    Stale,
}

public sealed record CodeIntelligenceIssue(
    CodeIntelligenceIssueCode Code,
    CodeIntelligenceMessage Message);

public sealed record CodeIntelligenceOpenRequest(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceRootPath RootPath,
    CodeIntelligenceEntryPoint EntryPoint,
    CodeIntelligenceSourceKind SourceKind);

public sealed record CodeIntelligenceSessionResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId? SessionId,
    CodeIntelligenceResultState State,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

public sealed record CodeIntelligenceDocumentSnapshot(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBaselineHash BaselineHash,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceText Text);

public sealed record CodeIntelligencePosition(
    int Line,
    int Character);

public sealed record CodeIntelligenceRange(
    CodeIntelligencePosition Start,
    CodeIntelligencePosition End);

public enum CodeIntelligenceDiagnosticSeverity
{
    Hidden,
    Information,
    Warning,
    Error,
}

public sealed record CodeIntelligenceDiagnostic(
    CodeIntelligenceDiagnosticId Id,
    CodeIntelligenceMessage Message,
    CodeIntelligenceDiagnosticSource Source,
    CodeIntelligenceProjectName? Project,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceRange Range,
    CodeIntelligenceDiagnosticSeverity Severity);

public sealed record CodeIntelligenceDiagnosticResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    IReadOnlyList<CodeIntelligenceDiagnostic> Diagnostics,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

public sealed record CodeIntelligenceCandidateEdit(
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBaselineHash BaselineHash,
    CodeIntelligenceText Text);

public sealed record CodeIntelligenceValidationRequest(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    IReadOnlyList<CodeIntelligenceCandidateEdit> Edits);

public enum CodeIntelligenceValidationDisposition
{
    Validated,
    Rejected,
    NotApplicable,
}

public enum CodeIntelligenceDiagnosticDeltaKind
{
    Retained,
    Resolved,
    Introduced,
}

public sealed record CodeIntelligenceValidationDiagnostic(
    CodeIntelligenceDiagnosticDeltaKind Kind,
    CodeIntelligenceDiagnostic Diagnostic);

public sealed record CodeIntelligenceValidationResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceResultState State,
    CodeIntelligenceValidationDisposition Disposition,
    IReadOnlyList<CodeIntelligenceValidationDiagnostic> Diagnostics,
    IReadOnlyList<CodeIntelligenceIssue> Issues);
