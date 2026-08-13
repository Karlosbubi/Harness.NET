namespace Harness.BusinessLogic.CodeIntelligence;

public sealed record WorkbenchCodeInteractiveSnapshot(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBaselineHash BaselineHash,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeText Text,
    WorkbenchCodePosition Position);

public enum WorkbenchCodeCompletionTriggerKind
{
    Invoke,
    Insertion,
}

public sealed record WorkbenchCodeCompletionListId(string Value);

public sealed record WorkbenchCodeCompletionItemId(string Value);

public enum WorkbenchCodeSymbolKind
{
    Keyword,
    Namespace,
    Class,
    Interface,
    Structure,
    Enumeration,
    Delegate,
    Method,
    ExtensionMethod,
    Constructor,
    Property,
    Field,
    Event,
    Constant,
    Local,
    Parameter,
    TypeParameter,
    Snippet,
    Region,
    Other,
}

public sealed record WorkbenchCodeCompletionRequest(
    WorkbenchCodeInteractiveSnapshot Snapshot,
    WorkbenchCodeCompletionTriggerKind TriggerKind,
    char? TriggerCharacter);

public sealed record WorkbenchCodeCompletionItem(
    WorkbenchCodeCompletionItemId Id,
    WorkbenchCodeMessage DisplayText,
    WorkbenchCodeMessage FilterText,
    WorkbenchCodeMessage SortText,
    WorkbenchCodeMessage Description,
    WorkbenchCodeSymbolKind Kind,
    IReadOnlyList<char> CommitCharacters,
    bool IsRecommended);

public sealed record WorkbenchCodeCompletionView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    WorkbenchCodeCompletionListId? ListId,
    WorkbenchCodeRange ApplicableRange,
    IReadOnlyList<WorkbenchCodeCompletionItem> Items,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public sealed record WorkbenchCodeCompletionCommitRequest(
    WorkbenchCodeInteractiveSnapshot Snapshot,
    WorkbenchCodeCompletionListId ListId,
    WorkbenchCodeCompletionItemId ItemId,
    char? CommitCharacter);

public sealed record WorkbenchCodeTextChange(
    WorkbenchCodeRange Range,
    WorkbenchCodeText Text);

public sealed record WorkbenchCodeCompletionCommitView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeTextChange> Changes,
    WorkbenchCodePosition? NewPosition,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public sealed record WorkbenchCodeQuickInfoView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    WorkbenchCodeRange? ApplicableRange,
    IReadOnlyList<WorkbenchCodeMessage> Sections,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public sealed record WorkbenchCodeSignatureParameter(
    WorkbenchCodeMessage Name,
    WorkbenchCodeMessage Display,
    WorkbenchCodeMessage Documentation);

public sealed record WorkbenchCodeSignatureItem(
    WorkbenchCodeMessage Display,
    WorkbenchCodeMessage Documentation,
    IReadOnlyList<WorkbenchCodeSignatureParameter> Parameters);

public sealed record WorkbenchCodeSignatureHelpView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeSignatureItem> Signatures,
    int SelectedSignature,
    int SelectedParameter,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public enum WorkbenchCodeDestinationKind
{
    Source,
    Generated,
    Metadata,
    Unavailable,
}

public sealed record WorkbenchCodeVirtualDocumentId(string Value);

public enum WorkbenchCodeVirtualDocumentKind
{
    GeneratedSource,
    MetadataSignature,
}

public sealed record WorkbenchCodeProjectVersion(string Value);
public sealed record WorkbenchCodeTargetFramework(string Value);
public sealed record WorkbenchCodeBuildConfiguration(string Value);
public sealed record WorkbenchCodeAssemblyIdentity(string Value);
public sealed record WorkbenchCodeCompilationIdentity(string Value);

public sealed record WorkbenchCodeVirtualDocumentOrigin(
    WorkbenchCodeMessage Project,
    WorkbenchCodeProjectVersion ProjectVersion,
    WorkbenchCodeTargetFramework TargetFramework,
    WorkbenchCodeBuildConfiguration Configuration,
    WorkbenchCodeAssemblyIdentity Assembly,
    WorkbenchCodeCompilationIdentity Compilation);

public sealed record WorkbenchCodeSymbolDestination(
    WorkbenchCodeDestinationKind Kind,
    WorkbenchCodeMessage Display,
    WorkbenchCodeDocumentPath? Path,
    WorkbenchCodeRange? Range,
    WorkbenchCodeVirtualDocumentId? VirtualDocumentId = null);

public sealed record WorkbenchCodeNavigationView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeSymbolDestination> Destinations,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public sealed record WorkbenchCodeVirtualDocumentRequest(
    WorkbenchCodeInteractiveSnapshot Snapshot,
    WorkbenchCodeVirtualDocumentId Id);

public sealed record WorkbenchCodeVirtualDocumentView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath SourcePath,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    WorkbenchCodeVirtualDocumentId Id,
    WorkbenchCodeVirtualDocumentKind? Kind,
    WorkbenchCodeMessage? Title,
    WorkbenchCodeText? Text,
    WorkbenchCodeRange? SelectionRange,
    WorkbenchCodeVirtualDocumentOrigin? Origin,
    bool IsReadOnly,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public enum WorkbenchCodeInspectionKind
{
    SyntaxTree,
    Symbol,
    GeneratedSource,
    IntermediateLanguage,
}

public sealed record WorkbenchCodeInspectionRequest(
    WorkbenchCodeInteractiveSnapshot Snapshot,
    WorkbenchCodeInspectionKind Kind);

public sealed record WorkbenchCodeInspectionView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    WorkbenchCodeInspectionKind Kind,
    WorkbenchCodeMessage? Title,
    WorkbenchCodeText? Text,
    WorkbenchCodeVirtualDocumentOrigin? Origin,
    bool IsReadOnly,
    bool IsTruncated,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public enum WorkbenchCodeSemanticRelation
{
    Symbol, IncomingCall, OutgoingCall, BaseType, DerivedType, Override, AssociatedTest,
}

public sealed record WorkbenchCodeSemanticQuery(
    WorkbenchCodeInteractiveSnapshot Snapshot,
    string? Query,
    int MaximumResults,
    int Offset);

public sealed record WorkbenchCodeSemanticItem(
    WorkbenchCodeSemanticRelation Relation,
    WorkbenchCodeMessage Display,
    WorkbenchCodeSymbolDestination Destination);

public sealed record WorkbenchCodeSemanticView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeSemanticItem> Items,
    int? Continuation,
    bool IsTruncated,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public enum WorkbenchCodeClassificationKind
{
    Text,
    Keyword,
    ControlKeyword,
    Comment,
    DocumentationComment,
    String,
    Number,
    Preprocessor,
    Namespace,
    Type,
    Method,
    Property,
    Field,
    Event,
    Parameter,
    Local,
    TypeParameter,
    Operator,
    Punctuation,
    Identifier,
    ExcludedCode,
}

public enum WorkbenchCodeOccurrenceKind { Definition, Read, Write }

public enum WorkbenchCodeFoldingKind { Namespace, Type, Member, Block, Region, Comment }

public sealed record WorkbenchCodeClassifiedSpan(
    WorkbenchCodeRange Range,
    WorkbenchCodeClassificationKind Kind);

public sealed record WorkbenchCodeOccurrence(
    WorkbenchCodeRange Range,
    WorkbenchCodeOccurrenceKind Kind);

public sealed record WorkbenchCodeFoldingRange(
    WorkbenchCodeRange Range,
    WorkbenchCodeFoldingKind Kind,
    WorkbenchCodeMessage Display,
    bool IsDefaultCollapsed);

public sealed record WorkbenchCodeOutlineItem(
    WorkbenchCodeSymbolKind Kind,
    WorkbenchCodeMessage Display,
    WorkbenchCodeRange Range,
    WorkbenchCodeRange SelectionRange,
    int Depth);

public sealed record WorkbenchCodeBreadcrumb(
    WorkbenchCodeSymbolKind Kind,
    WorkbenchCodeMessage Display,
    WorkbenchCodeRange Range);

public enum WorkbenchCodeDocumentPresentationScope
{
    VisibleClassification,
    ClassificationAndStructure,
}

public enum WorkbenchCodeInlayHintKind { ParameterName, InferredType }

public enum WorkbenchCodeLensKind { References, Implementations, Tests, Run, Debug }

public enum WorkbenchExecutionTargetKind { ProjectEntryPoint }

public sealed record WorkbenchExecutionTarget(
    WorkbenchExecutionTargetKind Kind,
    WorkbenchCodeDocumentPath ProjectPath,
    WorkbenchCodeTargetFramework TargetFramework,
    WorkbenchCodeMessage DeclarationId,
    WorkbenchCodeDocumentPath SourcePath,
    WorkbenchCodeBaselineHash SourceBaseline,
    WorkbenchCodeBufferVersion BufferVersion);

public sealed record WorkbenchCodeInlayHintOptions(
    bool ShowParameterNames,
    bool ShowInferredTypes);

public sealed record WorkbenchCodeLensOptions(
    bool ShowReferences,
    bool ShowImplementations,
    bool ShowTests,
    bool ShowRun = false,
    bool ShowDebug = false);

public sealed record WorkbenchCodeInlayHint(
    WorkbenchCodePosition Position,
    WorkbenchCodeInlayHintKind Kind,
    WorkbenchCodeMessage Label,
    WorkbenchCodeMessage Tooltip);

public sealed record WorkbenchCodeLens(
    WorkbenchCodePosition Position,
    WorkbenchCodePosition Target,
    WorkbenchCodeLensKind Kind,
    WorkbenchCodeMessage Display,
    bool IsResolved,
    WorkbenchExecutionTarget? ExecutionTarget = null);

public sealed record WorkbenchCodeDocumentPresentationRequest(
    WorkbenchCodeInteractiveSnapshot Snapshot,
    WorkbenchCodeRange? VisibleRange,
    WorkbenchCodeDocumentPresentationScope Scope =
        WorkbenchCodeDocumentPresentationScope.ClassificationAndStructure,
    WorkbenchCodeInlayHintOptions? InlayHints = null,
    WorkbenchCodeLensOptions? CodeLens = null);

public sealed record WorkbenchCodeDocumentPresentationView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeClassifiedSpan> Classifications,
    IReadOnlyList<WorkbenchCodeFoldingRange> FoldingRanges,
    IReadOnlyList<WorkbenchCodeOutlineItem> Outline,
    IReadOnlyList<WorkbenchCodeBreadcrumb> Breadcrumbs,
    IReadOnlyList<WorkbenchCodeInlayHint> InlayHints,
    IReadOnlyList<WorkbenchCodeLens> CodeLenses,
    bool IsTruncated,
    IReadOnlyList<WorkbenchCodeIssue> Issues);

public sealed record WorkbenchCodeOccurrenceView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    WorkbenchCodeMessage? Symbol,
    IReadOnlyList<WorkbenchCodeOccurrence> Occurrences,
    bool IsTruncated,
    IReadOnlyList<WorkbenchCodeIssue> Issues);
