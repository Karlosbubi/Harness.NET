namespace Harness.DataAccess.CodeIntelligence;

public sealed record CodeIntelligenceRenameName(string Value);

public sealed record CodeIntelligenceSymbolIdentity(string Value);

public sealed record CodeIntelligenceTransformationFingerprint(string Value);

public sealed record CodeIntelligenceImportNamespace(string Value);

public sealed record CodeIntelligenceImportSymbol(string Value);

public sealed record CodeIntelligenceCodeActionId(string Value);

public sealed record CodeIntelligenceCodeActionTitle(string Value);

public enum CodeIntelligenceClosedCodeActionKind
{
    ImplementInterface,
    ImplementAbstractMembers,
    AddExplicitCast,
    AssignOutParameters,
    GenerateConstructor,
    GenerateVariable,
    AddParameter,
    FixReturnType,
    MakeMemberStatic,
    MakeTypeAbstract,
    MakeTypePartial,
    RemoveUnnecessaryCast,
    SimplifyTypeName,
    UseNullPropagation,
    UseCompoundAssignment,
    AddBraces,
    InlineDeclaration,
    UseObjectInitializer,
    UseCollectionInitializer,
    ConvertAutoPropertyToFullProperty,
    ConvertLoop,
    ConvertIfToSwitch,
    ConvertLocalFunctionToMethod,
    InlineTemporary,
    IntroduceLocal,
    InvertConditional,
    MoveDeclarationNearReference,
    ConvertNamespace,
    AddParameterCheck,
    InitializeMemberFromParameter,
    IntroduceUsingStatement,
    UseExplicitType,
    UseImplicitType,
    UseExpressionBody,
    ExtractMethod,
    IntroduceVariable,
    GenerateEqualityMembers,
    GenerateOverrides,
    ReplaceMemberKind,
}

public enum CodeIntelligenceCodeActionScope
{
    Occurrence,
    Document,
}

public enum CodeIntelligenceFormattingTrigger
{
    Paste,
    Semicolon,
    CloseBrace,
    NewLine,
}

public enum CodeIntelligenceDocumentTransformationKind
{
    FormatDocument,
    FormatSelection,
    FormatChangedSpans,
    FormatPaste,
    FormatOnType,
    OrganizeImports,
    RemoveUnusedImports,
    AddMissingImport,
    ApplyCodeAction,
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
    OutsideSourceContext,
    Uneditable,
    InconsistentLinkedFile,
    TooManyFiles,
    TooLarge,
}

public sealed record CodeIntelligenceDocumentTransformationConflict(
    CodeIntelligenceDocumentTransformationConflictKind Kind,
    CodeIntelligenceMessage Message,
    CodeIntelligenceDocumentPath? Path = null);

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
    CodeIntelligenceImportNamespace? ImportNamespace = null,
    CodeIntelligenceFormattingTrigger? FormattingTrigger = null,
    CodeIntelligenceCodeActionId? CodeActionId = null,
    CodeIntelligenceCodeActionScope? CodeActionScope = null);

public sealed record CodeIntelligenceDocumentTransformationPreviewResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    CodeIntelligenceTransformationDisposition Disposition,
    CodeIntelligenceDocumentTransformationKind Kind,
    CodeIntelligenceRange? Range,
    IReadOnlyList<CodeIntelligenceDocumentTransformationEdit> Edits,
    IReadOnlyList<CodeIntelligenceDocumentTransformationConflict> Conflicts,
    IReadOnlyList<CodeIntelligenceValidationDiagnostic> Diagnostics,
    CodeIntelligenceTransformationFingerprint? Fingerprint,
    IReadOnlyList<CodeIntelligenceIssue> Issues,
    CodeIntelligenceImportNamespace? ImportNamespace = null,
    CodeIntelligenceFormattingTrigger? FormattingTrigger = null,
    CodeIntelligenceCodeActionId? CodeActionId = null,
    CodeIntelligenceCodeActionScope? CodeActionScope = null)
{
    public CodeIntelligenceDocumentTransformationEdit? Edit =>
        Edits.Count == 1 ? Edits[0] : null;
}

public sealed record CodeIntelligenceCodeActionCandidate(
    CodeIntelligenceCodeActionId Id,
    CodeIntelligenceClosedCodeActionKind Kind,
    CodeIntelligenceCodeActionScope Scope,
    CodeIntelligenceCodeActionTitle Title,
    CodeIntelligenceDiagnosticId? DiagnosticId,
    CodeIntelligenceRange Range,
    int AffectedFileCount = 1,
    bool ChangesActiveDocument = true);

public sealed record CodeIntelligenceCodeActionRequest(
    CodeIntelligenceInteractiveSnapshot Snapshot,
    CodeIntelligenceRange? Range = null);

public sealed record CodeIntelligenceCodeActionResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    IReadOnlyList<CodeIntelligenceCodeActionCandidate> Candidates,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

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
