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

public sealed record WorkbenchCodeSymbolDestination(
    WorkbenchCodeDestinationKind Kind,
    WorkbenchCodeMessage Display,
    WorkbenchCodeDocumentPath? Path,
    WorkbenchCodeRange? Range);

public sealed record WorkbenchCodeNavigationView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeSymbolDestination> Destinations,
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
