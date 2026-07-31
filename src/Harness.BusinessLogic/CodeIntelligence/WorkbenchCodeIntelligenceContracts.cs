using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.CodeIntelligence;

public sealed record WorkbenchCodeContextId(string Value);

public sealed record WorkbenchCodeSessionId(string Value);

public sealed record WorkbenchCodeEntryPoint(string Value);

public sealed record WorkbenchCodeDocumentPath(string Value);

public sealed record WorkbenchCodeBaselineHash(string Value);

public sealed record WorkbenchCodeBufferVersion(long Value);

public sealed record WorkbenchCodeText(string Value);

public sealed record WorkbenchCodeIssueCode(string Value);

public sealed record WorkbenchCodeMessage(string Value);

public sealed record WorkbenchCodeDiagnosticId(string Value);

public sealed record WorkbenchCodeDiagnosticSource(string Value);

public sealed record WorkbenchCodeProjectName(string Value);

public enum WorkbenchCodeResultState
{
    Ready,
    Loading,
    Degraded,
    Cancelled,
    Failed,
    Stale,
}

public enum WorkbenchCodeLoadStage
{
    SelectingSdk,
    RegisteringMSBuild,
    LoadingEntryPoint,
    EvaluatingProjects,
    Ready,
}

public sealed record WorkbenchCodeLoadProgress(
    WorkbenchCodeContextId ContextId,
    WorkbenchCodeLoadStage Stage,
    WorkbenchCodeMessage Message);

public sealed record WorkbenchCodeIssue(
    WorkbenchCodeIssueCode Code,
    WorkbenchCodeMessage Message);

public sealed record WorkbenchCodeSessionRequest(
    WorkspaceId WorkspaceId,
    GoalId? GoalId,
    WorkbenchCodeEntryPoint EntryPoint);

public sealed record WorkbenchCodeSessionView(
    WorkbenchCodeContextId? ContextId,
    WorkbenchCodeSessionId? SessionId,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public sealed record WorkbenchCodeDocumentSnapshot(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBaselineHash BaselineHash,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeText Text);

public sealed record WorkbenchCodePosition(
    int Line,
    int Character);

public sealed record WorkbenchCodeRange(
    WorkbenchCodePosition Start,
    WorkbenchCodePosition End);

public enum WorkbenchCodeDiagnosticSeverity
{
    Hidden,
    Information,
    Warning,
    Error,
}

public sealed record WorkbenchCodeDiagnostic(
    WorkbenchCodeDiagnosticId Id,
    WorkbenchCodeMessage Message,
    WorkbenchCodeDiagnosticSource Source,
    WorkbenchCodeProjectName? Project,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeRange Range,
    WorkbenchCodeDiagnosticSeverity Severity);

public sealed record WorkbenchCodeDiagnosticView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeDiagnostic> Diagnostics,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public sealed record WorkbenchCodeCandidateEdit(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBaselineHash BaselineHash,
    WorkbenchCodeText Text);

public sealed record WorkbenchCodeValidationRequest(
    WorkbenchCodeSessionId SessionId,
    IReadOnlyList<WorkbenchCodeCandidateEdit> Edits);

public enum WorkbenchCodeValidationDisposition
{
    Validated,
    Rejected,
    NotApplicable,
}

public enum WorkbenchCodeDiagnosticDeltaKind
{
    Retained,
    Resolved,
    Introduced,
}

public sealed record WorkbenchCodeValidationDiagnostic(
    WorkbenchCodeDiagnosticDeltaKind Kind,
    WorkbenchCodeDiagnostic Diagnostic);

public sealed record WorkbenchCodeValidationView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeResultState State,
    WorkbenchCodeValidationDisposition Disposition,
    IReadOnlyList<WorkbenchCodeValidationDiagnostic> Diagnostics,
    IReadOnlyList<WorkbenchCodeIssue> Issues);
