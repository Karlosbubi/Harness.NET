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

public sealed record CodeIntelligenceSymbolDestination(
    CodeIntelligenceDestinationKind Kind,
    CodeIntelligenceMessage Display,
    CodeIntelligenceDocumentPath? Path,
    CodeIntelligenceRange? Range);

public sealed record CodeIntelligenceNavigationResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceBufferVersion BufferVersion,
    CodeIntelligenceResultState State,
    IReadOnlyList<CodeIntelligenceSymbolDestination> Destinations,
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
