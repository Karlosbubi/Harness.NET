namespace Harness.DataAccess.CodeIntelligence;

public sealed record CodeIntelligenceRenameName(string Value);

public sealed record CodeIntelligenceSymbolIdentity(string Value);

public sealed record CodeIntelligenceTransformationFingerprint(string Value);

public enum CodeIntelligenceTransformationDisposition
{
    Ready,
    Conflicted,
    Rejected,
}

public enum CodeIntelligenceRenameConflictKind
{
    Semantic,
    Generated,
    Metadata,
    OutsideSourceContext,
    Uneditable,
    InconsistentLinkedFile,
    TooManyFiles,
    TooLarge,
}

public sealed record CodeIntelligenceRenameConflict(
    CodeIntelligenceRenameConflictKind Kind,
    CodeIntelligenceMessage Message,
    CodeIntelligenceDocumentPath? Path);

public sealed record CodeIntelligenceRenameEdit(
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBaselineHash BaselineHash,
    CodeIntelligenceText OriginalText,
    CodeIntelligenceText Text,
    int ReplacementCount);

public sealed record CodeIntelligenceRenamePreviewRequest(
    CodeIntelligenceInteractiveSnapshot Snapshot,
    CodeIntelligenceRenameName NewName);

public sealed record CodeIntelligenceRenamePreviewResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    CodeIntelligenceTransformationDisposition Disposition,
    CodeIntelligenceSymbolIdentity? Symbol,
    CodeIntelligenceRenameName NewName,
    IReadOnlyList<CodeIntelligenceRenameEdit> Edits,
    IReadOnlyList<CodeIntelligenceRenameConflict> Conflicts,
    IReadOnlyList<CodeIntelligenceValidationDiagnostic> Diagnostics,
    CodeIntelligenceTransformationFingerprint? Fingerprint,
    IReadOnlyList<CodeIntelligenceIssue> Issues);
