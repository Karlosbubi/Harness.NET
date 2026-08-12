namespace Harness.DataAccess.CodeIntelligence;

public sealed record CodeIntelligenceRenameName(string Value);

public sealed record CodeIntelligenceSymbolIdentity(string Value);

public sealed record CodeIntelligenceTransformationFingerprint(string Value);

public sealed record CodeIntelligenceImportNamespace(string Value);

public sealed record CodeIntelligenceImportSymbol(string Value);

public enum CodeIntelligenceDocumentTransformationKind
{
    FormatDocument,
    FormatSelection,
    OrganizeImports,
    RemoveUnusedImports,
    AddMissingImport,
}

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

public enum CodeIntelligenceDocumentTransformationConflictKind
{
    Semantic,
    Generated,
    Uneditable,
    TooLarge,
}

public sealed record CodeIntelligenceDocumentTransformationConflict(
    CodeIntelligenceDocumentTransformationConflictKind Kind,
    CodeIntelligenceMessage Message);

public sealed record CodeIntelligenceDocumentTransformationEdit(
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBaselineHash BaselineHash,
    CodeIntelligenceText OriginalText,
    CodeIntelligenceText Text,
    int ReplacementCount);

public sealed record CodeIntelligenceDocumentTransformationPreviewRequest(
    CodeIntelligenceInteractiveSnapshot Snapshot,
    CodeIntelligenceDocumentTransformationKind Kind,
    CodeIntelligenceRange? Range,
    CodeIntelligenceImportNamespace? ImportNamespace = null);

public sealed record CodeIntelligenceDocumentTransformationPreviewResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    CodeIntelligenceTransformationDisposition Disposition,
    CodeIntelligenceDocumentTransformationKind Kind,
    CodeIntelligenceRange? Range,
    CodeIntelligenceDocumentTransformationEdit? Edit,
    IReadOnlyList<CodeIntelligenceDocumentTransformationConflict> Conflicts,
    IReadOnlyList<CodeIntelligenceValidationDiagnostic> Diagnostics,
    CodeIntelligenceTransformationFingerprint? Fingerprint,
    IReadOnlyList<CodeIntelligenceIssue> Issues,
    CodeIntelligenceImportNamespace? ImportNamespace = null);

public sealed record CodeIntelligenceMissingImportCandidate(
    CodeIntelligenceImportNamespace Namespace,
    CodeIntelligenceImportSymbol Symbol,
    CodeIntelligenceRange Range);

public sealed record CodeIntelligenceMissingImportResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    IReadOnlyList<CodeIntelligenceMissingImportCandidate> Candidates,
    IReadOnlyList<CodeIntelligenceIssue> Issues);
