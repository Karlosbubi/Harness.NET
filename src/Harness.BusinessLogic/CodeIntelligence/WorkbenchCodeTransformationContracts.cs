namespace Harness.BusinessLogic.CodeIntelligence;

public sealed record WorkbenchCodeRenameName(string Value);

public sealed record WorkbenchCodeSymbolIdentity(string Value);

public sealed record WorkbenchCodeTransformationFingerprint(string Value);

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
