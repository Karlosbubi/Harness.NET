namespace Harness.DataAccess.CodeIntelligence;

public sealed record CodeIntelligenceInteractiveSnapshot(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBaselineHash BaselineHash,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceText Text,
    CodeIntelligencePosition Position);

public enum CodeIntelligenceCompletionTriggerKind
{
    Invoke,
    Insertion,
}

public sealed record CodeIntelligenceCompletionListId(string Value);

public sealed record CodeIntelligenceCompletionItemId(string Value);

public enum CodeIntelligenceSymbolKind
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

public sealed record CodeIntelligenceCompletionRequest(
    CodeIntelligenceInteractiveSnapshot Snapshot,
    CodeIntelligenceCompletionTriggerKind TriggerKind,
    char? TriggerCharacter);

public sealed record CodeIntelligenceCompletionItem(
    CodeIntelligenceCompletionItemId Id,
    CodeIntelligenceMessage DisplayText,
    CodeIntelligenceMessage FilterText,
    CodeIntelligenceMessage SortText,
    CodeIntelligenceMessage Description,
    CodeIntelligenceSymbolKind Kind,
    IReadOnlyList<char> CommitCharacters,
    bool IsRecommended);

public sealed record CodeIntelligenceCompletionResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    CodeIntelligenceCompletionListId? ListId,
    CodeIntelligenceRange ApplicableRange,
    IReadOnlyList<CodeIntelligenceCompletionItem> Items,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

public sealed record CodeIntelligenceCompletionCommitRequest(
    CodeIntelligenceInteractiveSnapshot Snapshot,
    CodeIntelligenceCompletionListId ListId,
    CodeIntelligenceCompletionItemId ItemId,
    char? CommitCharacter);

public sealed record CodeIntelligenceTextChange(
    CodeIntelligenceRange Range,
    CodeIntelligenceText Text);

public sealed record CodeIntelligenceCompletionCommitResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    IReadOnlyList<CodeIntelligenceTextChange> Changes,
    CodeIntelligencePosition? NewPosition,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

public sealed record CodeIntelligenceQuickInfoResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    CodeIntelligenceRange? ApplicableRange,
    IReadOnlyList<CodeIntelligenceMessage> Sections,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

public sealed record CodeIntelligenceSignatureParameter(
    CodeIntelligenceMessage Name,
    CodeIntelligenceMessage Display,
    CodeIntelligenceMessage Documentation);

public sealed record CodeIntelligenceSignatureItem(
    CodeIntelligenceMessage Display,
    CodeIntelligenceMessage Documentation,
    IReadOnlyList<CodeIntelligenceSignatureParameter> Parameters);

public sealed record CodeIntelligenceSignatureHelpResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    IReadOnlyList<CodeIntelligenceSignatureItem> Signatures,
    int SelectedSignature,
    int SelectedParameter,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

public enum CodeIntelligenceDestinationKind
{
    Source,
    Generated,
    Metadata,
    Unavailable,
}

public sealed record CodeIntelligenceVirtualDocumentId(string Value);

public enum CodeIntelligenceVirtualDocumentKind
{
    GeneratedSource,
    MetadataSignature,
    DecompiledSource,
}

public sealed record CodeIntelligenceProjectVersion(string Value);
public sealed record CodeIntelligenceTargetFramework(string Value);
public sealed record CodeIntelligenceBuildConfiguration(string Value);
public sealed record CodeIntelligenceAssemblyIdentity(string Value);
public sealed record CodeIntelligenceCompilationIdentity(string Value);

public sealed record CodeIntelligenceVirtualDocumentOrigin(
    CodeIntelligenceProjectName Project,
    CodeIntelligenceProjectVersion ProjectVersion,
    CodeIntelligenceTargetFramework TargetFramework,
    CodeIntelligenceBuildConfiguration Configuration,
    CodeIntelligenceAssemblyIdentity Assembly,
    CodeIntelligenceCompilationIdentity Compilation);

public sealed record CodeIntelligenceSymbolDestination(
    CodeIntelligenceDestinationKind Kind,
    CodeIntelligenceMessage Display,
    CodeIntelligenceDocumentPath? Path,
    CodeIntelligenceRange? Range,
    CodeIntelligenceVirtualDocumentId? VirtualDocumentId = null);

public sealed record CodeIntelligenceNavigationResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    IReadOnlyList<CodeIntelligenceSymbolDestination> Destinations,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

public sealed record CodeIntelligenceVirtualDocumentRequest(
    CodeIntelligenceInteractiveSnapshot Snapshot,
    CodeIntelligenceVirtualDocumentId Id);

public sealed record CodeIntelligenceVirtualDocumentResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath SourcePath,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    CodeIntelligenceVirtualDocumentId Id,
    CodeIntelligenceVirtualDocumentKind? Kind,
    CodeIntelligenceMessage? Title,
    CodeIntelligenceText? Text,
    CodeIntelligenceRange? SelectionRange,
    CodeIntelligenceVirtualDocumentOrigin? Origin,
    bool IsReadOnly,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

public enum CodeIntelligenceInspectionKind
{
    SyntaxTree,
    Symbol,
    GeneratedSource,
    IntermediateLanguage,
}

public sealed record CodeIntelligenceInspectionRequest(
    CodeIntelligenceInteractiveSnapshot Snapshot,
    CodeIntelligenceInspectionKind Kind);

public sealed record CodeIntelligenceInspectionResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    CodeIntelligenceInspectionKind Kind,
    CodeIntelligenceMessage? Title,
    CodeIntelligenceText? Text,
    CodeIntelligenceVirtualDocumentOrigin? Origin,
    bool IsReadOnly,
    bool IsTruncated,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

public enum CodeIntelligenceSemanticRelation
{
    Symbol,
    IncomingCall,
    OutgoingCall,
    BaseType,
    DerivedType,
    Override,
    AssociatedTest,
}

public sealed record CodeIntelligenceSemanticQuery(
    CodeIntelligenceInteractiveSnapshot Snapshot,
    string? Query,
    int MaximumResults,
    int Offset);

public sealed record CodeIntelligenceSemanticItem(
    CodeIntelligenceSemanticRelation Relation,
    CodeIntelligenceMessage Display,
    CodeIntelligenceSymbolDestination Destination);

public sealed record CodeIntelligenceSemanticResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    IReadOnlyList<CodeIntelligenceSemanticItem> Items,
    int? Continuation,
    bool IsTruncated,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

public enum CodeIntelligenceClassificationKind
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

public enum CodeIntelligenceOccurrenceKind
{
    Definition,
    Read,
    Write,
}

public enum CodeIntelligenceFoldingKind
{
    Namespace,
    Type,
    Member,
    Block,
    Region,
    Comment,
}

public sealed record CodeIntelligenceClassifiedSpan(
    CodeIntelligenceRange Range,
    CodeIntelligenceClassificationKind Kind);

public sealed record CodeIntelligenceOccurrence(
    CodeIntelligenceRange Range,
    CodeIntelligenceOccurrenceKind Kind);

public sealed record CodeIntelligenceFoldingRange(
    CodeIntelligenceRange Range,
    CodeIntelligenceFoldingKind Kind,
    CodeIntelligenceMessage Display,
    bool IsDefaultCollapsed);

public sealed record CodeIntelligenceOutlineItem(
    CodeIntelligenceSymbolKind Kind,
    CodeIntelligenceMessage Display,
    CodeIntelligenceRange Range,
    CodeIntelligenceRange SelectionRange,
    int Depth);

public sealed record CodeIntelligenceBreadcrumb(
    CodeIntelligenceSymbolKind Kind,
    CodeIntelligenceMessage Display,
    CodeIntelligenceRange Range);

public enum CodeIntelligenceDocumentPresentationScope
{
    VisibleClassification,
    ClassificationAndStructure,
}

public enum CodeIntelligenceInlayHintKind
{
    ParameterName,
    InferredType,
}

public enum CodeIntelligenceCodeLensKind
{
    References,
    Implementations,
    Tests,
    Run,
    Debug,
}

public enum CodeIntelligenceExecutionTargetKind
{
    ProjectEntryPoint,
}

public sealed record CodeIntelligenceExecutionTarget(
    CodeIntelligenceExecutionTargetKind Kind,
    CodeIntelligenceDocumentPath ProjectPath,
    CodeIntelligenceTargetFramework TargetFramework,
    CodeIntelligenceMessage DeclarationId,
    CodeIntelligenceDocumentPath SourcePath,
    CodeIntelligenceBaselineHash SourceBaseline,
    CodeIntelligenceBufferVersion BufferVersion);

public sealed record CodeIntelligenceInlayHintOptions(
    bool ShowParameterNames,
    bool ShowInferredTypes);

public sealed record CodeIntelligenceCodeLensOptions(
    bool ShowReferences,
    bool ShowImplementations,
    bool ShowTests,
    bool ShowRun = false,
    bool ShowDebug = false);

public sealed record CodeIntelligenceInlayHint(
    CodeIntelligencePosition Position,
    CodeIntelligenceInlayHintKind Kind,
    CodeIntelligenceMessage Label,
    CodeIntelligenceMessage Tooltip);

public sealed record CodeIntelligenceCodeLens(
    CodeIntelligencePosition Position,
    CodeIntelligencePosition Target,
    CodeIntelligenceCodeLensKind Kind,
    CodeIntelligenceMessage Display,
    bool IsResolved,
    CodeIntelligenceExecutionTarget? ExecutionTarget = null);

public sealed record CodeIntelligenceDocumentPresentationRequest(
    CodeIntelligenceInteractiveSnapshot Snapshot,
    CodeIntelligenceRange? VisibleRange,
    CodeIntelligenceDocumentPresentationScope Scope =
        CodeIntelligenceDocumentPresentationScope.ClassificationAndStructure,
    CodeIntelligenceInlayHintOptions? InlayHints = null,
    CodeIntelligenceCodeLensOptions? CodeLens = null);

public sealed record CodeIntelligenceDocumentPresentationResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    IReadOnlyList<CodeIntelligenceClassifiedSpan> Classifications,
    IReadOnlyList<CodeIntelligenceFoldingRange> FoldingRanges,
    IReadOnlyList<CodeIntelligenceOutlineItem> Outline,
    IReadOnlyList<CodeIntelligenceBreadcrumb> Breadcrumbs,
    IReadOnlyList<CodeIntelligenceInlayHint> InlayHints,
    IReadOnlyList<CodeIntelligenceCodeLens> CodeLenses,
    bool IsTruncated,
    IReadOnlyList<CodeIntelligenceIssue> Issues);

public sealed record CodeIntelligenceOccurrenceResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    CodeIntelligenceMessage? Symbol,
    IReadOnlyList<CodeIntelligenceOccurrence> Occurrences,
    bool IsTruncated,
    IReadOnlyList<CodeIntelligenceIssue> Issues);
