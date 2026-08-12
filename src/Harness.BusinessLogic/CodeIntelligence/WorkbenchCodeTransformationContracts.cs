namespace Harness.BusinessLogic.CodeIntelligence;

public sealed record WorkbenchCodeRenameName(string Value);

public sealed record WorkbenchCodeSymbolIdentity(string Value);

public sealed record WorkbenchCodeTransformationFingerprint(string Value);

public sealed record WorkbenchCodeImportNamespace(string Value);

public sealed record WorkbenchCodeImportSymbol(string Value);

public enum WorkbenchCodeFormattingTrigger
{
    Paste,
    Semicolon,
    CloseBrace,
    NewLine,
}

public enum WorkbenchCodeDocumentTransformationKind
{
    FormatDocument,
    FormatSelection,
    FormatChangedSpans,
    FormatPaste,
    FormatOnType,
    OrganizeImports,
    RemoveUnusedImports,
    AddMissingImport,
}

public enum WorkbenchCodeTransformationDisposition
{
    Ready,
    Conflicted,
    Rejected,
}

public enum WorkbenchCodeRenameConflictKind
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

public sealed record WorkbenchCodeRenameConflict(
    WorkbenchCodeRenameConflictKind Kind,
    WorkbenchCodeMessage Message,
    WorkbenchCodeDocumentPath? Path);

public sealed record WorkbenchCodeRenameEdit(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBaselineHash BaselineHash,
    WorkbenchCodeText OriginalText,
    WorkbenchCodeText Text,
    int ReplacementCount);

public sealed record WorkbenchCodeRenamePreviewRequest(
    WorkbenchCodeInteractiveSnapshot Snapshot,
    WorkbenchCodeRenameName NewName);

public sealed record WorkbenchCodeRenamePreviewView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    WorkbenchCodeTransformationDisposition Disposition,
    WorkbenchCodeSymbolIdentity? Symbol,
    WorkbenchCodeRenameName NewName,
    IReadOnlyList<WorkbenchCodeRenameEdit> Edits,
    IReadOnlyList<WorkbenchCodeRenameConflict> Conflicts,
    IReadOnlyList<WorkbenchCodeValidationDiagnostic> Diagnostics,
    WorkbenchCodeTransformationFingerprint? Fingerprint,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public enum WorkbenchCodeDocumentTransformationConflictKind
{
    Semantic,
    Generated,
    Uneditable,
    TooLarge,
}

public sealed record WorkbenchCodeDocumentTransformationConflict(
    WorkbenchCodeDocumentTransformationConflictKind Kind,
    WorkbenchCodeMessage Message);

public sealed record WorkbenchCodeDocumentTransformationEdit(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBaselineHash BaselineHash,
    WorkbenchCodeText OriginalText,
    WorkbenchCodeText Text,
    int ReplacementCount);

public sealed record WorkbenchCodeDocumentTransformationPreviewRequest(
    WorkbenchCodeInteractiveSnapshot Snapshot,
    WorkbenchCodeDocumentTransformationKind Kind,
    WorkbenchCodeRange? Range,
    WorkbenchCodeImportNamespace? ImportNamespace = null,
    WorkbenchCodeFormattingTrigger? FormattingTrigger = null);

public sealed record WorkbenchCodeDocumentTransformationPreviewView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    WorkbenchCodeTransformationDisposition Disposition,
    WorkbenchCodeDocumentTransformationKind Kind,
    WorkbenchCodeRange? Range,
    WorkbenchCodeDocumentTransformationEdit? Edit,
    IReadOnlyList<WorkbenchCodeDocumentTransformationConflict> Conflicts,
    IReadOnlyList<WorkbenchCodeValidationDiagnostic> Diagnostics,
    WorkbenchCodeTransformationFingerprint? Fingerprint,
    IReadOnlyList<WorkbenchCodeIssue> Issues,
    WorkbenchCodeImportNamespace? ImportNamespace = null,
    WorkbenchCodeFormattingTrigger? FormattingTrigger = null);

public sealed record WorkbenchCodeMissingImportCandidate(
    WorkbenchCodeImportNamespace Namespace,
    WorkbenchCodeImportSymbol Symbol,
    WorkbenchCodeRange Range);

public sealed record WorkbenchCodeMissingImportView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeMissingImportCandidate> Candidates,
    IReadOnlyList<WorkbenchCodeIssue> Issues);
