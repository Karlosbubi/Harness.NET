using Harness.DataAccess.CodeIntelligence;

namespace Harness.BusinessLogic.CodeIntelligence;

internal sealed partial class WorkbenchCodeIntelligenceService
{
    private static WorkbenchCodeRange Map(CodeIntelligenceRange range) => new(
        Map(range.Start), Map(range.End));

    private static WorkbenchCodePosition Map(CodeIntelligencePosition position) =>
        new(position.Line, position.Character);

    private static WorkbenchCodeSymbolKind Map(CodeIntelligenceSymbolKind kind) => kind switch
    {
        CodeIntelligenceSymbolKind.Keyword => WorkbenchCodeSymbolKind.Keyword,
        CodeIntelligenceSymbolKind.Namespace => WorkbenchCodeSymbolKind.Namespace,
        CodeIntelligenceSymbolKind.Class => WorkbenchCodeSymbolKind.Class,
        CodeIntelligenceSymbolKind.Interface => WorkbenchCodeSymbolKind.Interface,
        CodeIntelligenceSymbolKind.Structure => WorkbenchCodeSymbolKind.Structure,
        CodeIntelligenceSymbolKind.Enumeration => WorkbenchCodeSymbolKind.Enumeration,
        CodeIntelligenceSymbolKind.Delegate => WorkbenchCodeSymbolKind.Delegate,
        CodeIntelligenceSymbolKind.Method => WorkbenchCodeSymbolKind.Method,
        CodeIntelligenceSymbolKind.ExtensionMethod => WorkbenchCodeSymbolKind.ExtensionMethod,
        CodeIntelligenceSymbolKind.Constructor => WorkbenchCodeSymbolKind.Constructor,
        CodeIntelligenceSymbolKind.Property => WorkbenchCodeSymbolKind.Property,
        CodeIntelligenceSymbolKind.Field => WorkbenchCodeSymbolKind.Field,
        CodeIntelligenceSymbolKind.Event => WorkbenchCodeSymbolKind.Event,
        CodeIntelligenceSymbolKind.Constant => WorkbenchCodeSymbolKind.Constant,
        CodeIntelligenceSymbolKind.Local => WorkbenchCodeSymbolKind.Local,
        CodeIntelligenceSymbolKind.Parameter => WorkbenchCodeSymbolKind.Parameter,
        CodeIntelligenceSymbolKind.TypeParameter => WorkbenchCodeSymbolKind.TypeParameter,
        CodeIntelligenceSymbolKind.Snippet => WorkbenchCodeSymbolKind.Snippet,
        CodeIntelligenceSymbolKind.Region => WorkbenchCodeSymbolKind.Region,
        CodeIntelligenceSymbolKind.Other => WorkbenchCodeSymbolKind.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static WorkbenchCodeClassificationKind Map(
        CodeIntelligenceClassificationKind kind) => kind switch
        {
            CodeIntelligenceClassificationKind.Text => WorkbenchCodeClassificationKind.Text,
            CodeIntelligenceClassificationKind.Keyword => WorkbenchCodeClassificationKind.Keyword,
            CodeIntelligenceClassificationKind.ControlKeyword =>
                WorkbenchCodeClassificationKind.ControlKeyword,
            CodeIntelligenceClassificationKind.Comment => WorkbenchCodeClassificationKind.Comment,
            CodeIntelligenceClassificationKind.DocumentationComment =>
                WorkbenchCodeClassificationKind.DocumentationComment,
            CodeIntelligenceClassificationKind.String => WorkbenchCodeClassificationKind.String,
            CodeIntelligenceClassificationKind.Number => WorkbenchCodeClassificationKind.Number,
            CodeIntelligenceClassificationKind.Preprocessor =>
                WorkbenchCodeClassificationKind.Preprocessor,
            CodeIntelligenceClassificationKind.Namespace =>
                WorkbenchCodeClassificationKind.Namespace,
            CodeIntelligenceClassificationKind.Type => WorkbenchCodeClassificationKind.Type,
            CodeIntelligenceClassificationKind.Method => WorkbenchCodeClassificationKind.Method,
            CodeIntelligenceClassificationKind.Property => WorkbenchCodeClassificationKind.Property,
            CodeIntelligenceClassificationKind.Field => WorkbenchCodeClassificationKind.Field,
            CodeIntelligenceClassificationKind.Event => WorkbenchCodeClassificationKind.Event,
            CodeIntelligenceClassificationKind.Parameter =>
                WorkbenchCodeClassificationKind.Parameter,
            CodeIntelligenceClassificationKind.Local => WorkbenchCodeClassificationKind.Local,
            CodeIntelligenceClassificationKind.TypeParameter =>
                WorkbenchCodeClassificationKind.TypeParameter,
            CodeIntelligenceClassificationKind.Operator => WorkbenchCodeClassificationKind.Operator,
            CodeIntelligenceClassificationKind.Punctuation =>
                WorkbenchCodeClassificationKind.Punctuation,
            CodeIntelligenceClassificationKind.Identifier =>
                WorkbenchCodeClassificationKind.Identifier,
            CodeIntelligenceClassificationKind.ExcludedCode =>
                WorkbenchCodeClassificationKind.ExcludedCode,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeOccurrenceKind Map(CodeIntelligenceOccurrenceKind kind) =>
        kind switch
        {
            CodeIntelligenceOccurrenceKind.Definition => WorkbenchCodeOccurrenceKind.Definition,
            CodeIntelligenceOccurrenceKind.Read => WorkbenchCodeOccurrenceKind.Read,
            CodeIntelligenceOccurrenceKind.Write => WorkbenchCodeOccurrenceKind.Write,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeFoldingKind Map(CodeIntelligenceFoldingKind kind) => kind switch
    {
        CodeIntelligenceFoldingKind.Namespace => WorkbenchCodeFoldingKind.Namespace,
        CodeIntelligenceFoldingKind.Type => WorkbenchCodeFoldingKind.Type,
        CodeIntelligenceFoldingKind.Member => WorkbenchCodeFoldingKind.Member,
        CodeIntelligenceFoldingKind.Block => WorkbenchCodeFoldingKind.Block,
        CodeIntelligenceFoldingKind.Region => WorkbenchCodeFoldingKind.Region,
        CodeIntelligenceFoldingKind.Comment => WorkbenchCodeFoldingKind.Comment,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static WorkbenchCodeInlayHintKind Map(CodeIntelligenceInlayHintKind kind) =>
        kind switch
        {
            CodeIntelligenceInlayHintKind.ParameterName =>
                WorkbenchCodeInlayHintKind.ParameterName,
            CodeIntelligenceInlayHintKind.InferredType =>
                WorkbenchCodeInlayHintKind.InferredType,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeLensKind Map(CodeIntelligenceCodeLensKind kind) => kind switch
    {
        CodeIntelligenceCodeLensKind.References => WorkbenchCodeLensKind.References,
        CodeIntelligenceCodeLensKind.Implementations => WorkbenchCodeLensKind.Implementations,
        CodeIntelligenceCodeLensKind.Tests => WorkbenchCodeLensKind.Tests,
        CodeIntelligenceCodeLensKind.Run => WorkbenchCodeLensKind.Run,
        CodeIntelligenceCodeLensKind.Debug => WorkbenchCodeLensKind.Debug,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static WorkbenchExecutionTargetKind Map(
        CodeIntelligenceExecutionTargetKind kind) =>
        kind switch
        {
            CodeIntelligenceExecutionTargetKind.ProjectEntryPoint =>
                WorkbenchExecutionTargetKind.ProjectEntryPoint,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeDestinationKind Map(CodeIntelligenceDestinationKind kind) =>
        kind switch
        {
            CodeIntelligenceDestinationKind.Source => WorkbenchCodeDestinationKind.Source,
            CodeIntelligenceDestinationKind.Generated => WorkbenchCodeDestinationKind.Generated,
            CodeIntelligenceDestinationKind.Metadata => WorkbenchCodeDestinationKind.Metadata,
            CodeIntelligenceDestinationKind.Unavailable => WorkbenchCodeDestinationKind.Unavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeVirtualDocumentKind Map(
        CodeIntelligenceVirtualDocumentKind kind) => kind switch
        {
            CodeIntelligenceVirtualDocumentKind.GeneratedSource =>
                WorkbenchCodeVirtualDocumentKind.GeneratedSource,
            CodeIntelligenceVirtualDocumentKind.MetadataSignature =>
                WorkbenchCodeVirtualDocumentKind.MetadataSignature,
            CodeIntelligenceVirtualDocumentKind.DecompiledSource =>
                WorkbenchCodeVirtualDocumentKind.DecompiledSource,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static CodeIntelligenceInspectionKind Map(WorkbenchCodeInspectionKind kind) =>
        kind switch
        {
            WorkbenchCodeInspectionKind.SyntaxTree => CodeIntelligenceInspectionKind.SyntaxTree,
            WorkbenchCodeInspectionKind.Symbol => CodeIntelligenceInspectionKind.Symbol,
            WorkbenchCodeInspectionKind.GeneratedSource =>
                CodeIntelligenceInspectionKind.GeneratedSource,
            WorkbenchCodeInspectionKind.IntermediateLanguage =>
                CodeIntelligenceInspectionKind.IntermediateLanguage,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeInspectionKind Map(CodeIntelligenceInspectionKind kind) =>
        kind switch
        {
            CodeIntelligenceInspectionKind.SyntaxTree => WorkbenchCodeInspectionKind.SyntaxTree,
            CodeIntelligenceInspectionKind.Symbol => WorkbenchCodeInspectionKind.Symbol,
            CodeIntelligenceInspectionKind.GeneratedSource =>
                WorkbenchCodeInspectionKind.GeneratedSource,
            CodeIntelligenceInspectionKind.IntermediateLanguage =>
                WorkbenchCodeInspectionKind.IntermediateLanguage,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeVirtualDocumentView VirtualDocumentFailure(
        WorkbenchCodeVirtualDocumentRequest request,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
            request.Snapshot.SessionId, request.Snapshot.Path, request.Snapshot.BufferVersion,
            state, request.Id, Kind: null, Title: null, Text: null, SelectionRange: null,
            Origin: null, IsReadOnly: true, [issue]);

    private static WorkbenchCodeInspectionView InspectionFailure(
        WorkbenchCodeInspectionRequest request,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
            request.Snapshot.SessionId, request.Snapshot.Path, request.Snapshot.BufferVersion,
            state, request.Kind, Title: null, Text: null, Origin: null, IsReadOnly: true,
            IsTruncated: false, [issue]);

    private static WorkbenchCodeCompletionView CompletionFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot?.SessionId ?? new(string.Empty), snapshot?.Path ?? new(string.Empty),
        snapshot?.BufferVersion ?? new(0), state, null,
        new(snapshot?.Position ?? new(0, 0), snapshot?.Position ?? new(0, 0)), [], [issue]);

    private static WorkbenchCodeCompletionCommitView CommitFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot?.SessionId ?? new(string.Empty), snapshot?.Path ?? new(string.Empty),
        snapshot?.BufferVersion ?? new(0), state, [], null, [issue]);

    private static WorkbenchCodeQuickInfoView QuickInfoFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot?.SessionId ?? new(string.Empty), snapshot?.Path ?? new(string.Empty),
        snapshot?.BufferVersion ?? new(0), state, null, [], [issue]);

    private static WorkbenchCodeSignatureHelpView SignatureFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot?.SessionId ?? new(string.Empty), snapshot?.Path ?? new(string.Empty),
        snapshot?.BufferVersion ?? new(0), state, [], 0, 0, [issue]);

    private static WorkbenchCodeNavigationView NavigationFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot?.SessionId ?? new(string.Empty), snapshot?.Path ?? new(string.Empty),
        snapshot?.BufferVersion ?? new(0), state, [], [issue]);}
