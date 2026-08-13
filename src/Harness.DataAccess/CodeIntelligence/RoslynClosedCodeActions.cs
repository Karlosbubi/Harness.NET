using System.Collections.Immutable;
using System.Composition.Hosting;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private const int MaximumCodeActions = 100;
    private const int MaximumDocumentCodeActionApplications = 250;
    private static readonly Lazy<ClosedCodeFixCatalog> ClosedCodeFixes =
        new(static () => new());

    internal static IReadOnlyList<string> ClosedCodeFixProviderNames() =>
        ClosedCodeFixes.Value.ProviderNames;

    internal static IReadOnlyList<string> ClosedCodeRefactoringProviderNames() =>
        ClosedCodeFixes.Value.RefactoringProviderNames;

    public async ValueTask<CodeIntelligenceCodeActionResult> GetCodeActionsAsync(
        CodeIntelligenceCodeActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        CodeIntelligenceInteractiveSnapshot snapshot = request.Snapshot;
        ActiveSession? session = MatchingSession(snapshot);
        if (session is null)
        {
            return CodeActionFailure(snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return CodeActionFailure(snapshot, prepared.State,
                    prepared.Issue.Code.Value, prepared.Issue.Message.Value);
            }

            if (!TryGetCodeActionSpan(prepared.Text!, prepared.Offset, request.Range,
                out TextSpan actionSpan))
            {
                return CodeActionFailure(snapshot, CodeIntelligenceResultState.Failed,
                    "invalid_code_action_range", "The code-action range is outside the document.");
            }

            IReadOnlyList<ClosedCodeAction> actions = await FindClosedCodeActionsAsync(
                prepared.Document!, actionSpan, cancellationToken);
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                actions.Select(action => new CodeIntelligenceCodeActionCandidate(
                    new(action.Id),
                    action.Descriptor.Kind,
                    action.Scope,
                    new(Bound(action.Action.Title, MaximumIssueLength)),
                    action.Diagnostic is null ? null : new(action.Diagnostic.Id),
                    Range(prepared.Text!, action.ContextSpan),
                    action.AffectedFileCount,
                    action.ChangesActiveDocument)).ToArray(),
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            return CodeActionFailure(snapshot, CodeIntelligenceResultState.Failed,
                "code_action_discovery_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    private static async ValueTask<IReadOnlyList<ClosedCodeAction>> FindClosedCodeActionsAsync(
        Document document,
        TextSpan actionSpan,
        CancellationToken cancellationToken)
    {
        SourceText source = await document.GetTextAsync(cancellationToken);
        int offset = actionSpan.Start;
        TextSpan caretLine = source.Lines.GetLineFromPosition(offset).SpanIncludingLineBreak;
        IReadOnlyList<Diagnostic> diagnostics = await DocumentDiagnosticsAsync(
            document, cancellationToken);
        List<ClosedCodeAction> result = [];
        foreach (Diagnostic diagnostic in diagnostics
            .Where(item => item.Location.SourceSpan.IntersectsWith(caretLine) ||
                Touches(item.Location.SourceSpan, offset))
            .OrderBy(item => item.Location.SourceSpan.Start)
            .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            foreach (ClosedCodeActionProvider provider in ClosedCodeFixes.Value.ProvidersFor(diagnostic.Id))
            {
                IReadOnlyList<CodeAction> actions = await RegisterActionsAsync(
                    provider.FixProvider!, document, diagnostic, cancellationToken);
                foreach (CodeAction action in actions)
                {
                    Solution? changed = await ChangedClosedSolutionAsync(
                        document, action, provider.AllowCrossDocument, cancellationToken);
                    if (changed is null)
                    {
                        continue;
                    }

                    result.Add(new(
                        CodeActionId(provider, diagnostic, action,
                            CodeIntelligenceCodeActionScope.Occurrence),
                        provider,
                        diagnostic,
                        action,
                        CodeIntelligenceCodeActionScope.Occurrence,
                        diagnostic.Location.SourceSpan,
                        ChangedDocumentCount(document.Project.Solution, changed),
                        ChangesDocument(document.Project.Solution, changed, document.Id)));
                    if (provider.AllowDocumentScope)
                    {
                        result.Add(new(
                            CodeActionId(provider, diagnostic, action,
                                CodeIntelligenceCodeActionScope.Document),
                            provider,
                            diagnostic,
                            action,
                            CodeIntelligenceCodeActionScope.Document,
                            diagnostic.Location.SourceSpan,
                            AffectedFileCount: 1,
                            ChangesActiveDocument: true));
                    }
                }
            }
        }

        foreach (ClosedCodeActionProvider provider in ClosedCodeFixes.Value.RefactoringProviders)
        {
            IReadOnlyList<CodeAction> actions = await RegisterRefactoringsAsync(
                provider.RefactoringProvider!, document, actionSpan, cancellationToken);
            foreach (CodeAction action in actions)
            {
                Solution? changed = await ChangedClosedSolutionAsync(
                    document, action, provider.AllowCrossDocument, cancellationToken);
                if (changed is null)
                {
                    continue;
                }

                result.Add(new(
                    CodeActionId(provider, diagnostic: null, action,
                        CodeIntelligenceCodeActionScope.Occurrence, actionSpan),
                    provider,
                    Diagnostic: null,
                    action,
                    CodeIntelligenceCodeActionScope.Occurrence,
                    actionSpan,
                    ChangedDocumentCount(document.Project.Solution, changed),
                    ChangesDocument(document.Project.Solution, changed, document.Id)));
            }
        }

        return result
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Action.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Scope)
            .Take(MaximumCodeActions)
            .ToArray();
    }

    private static async ValueTask<Solution?> ApplyClosedCodeActionAsync(
        Document baselineDocument,
        TextSpan actionSpan,
        CodeIntelligenceCodeActionId requestedId,
        CodeIntelligenceCodeActionScope requestedScope,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClosedCodeAction> available = await FindClosedCodeActionsAsync(
            baselineDocument, actionSpan, cancellationToken);
        ClosedCodeAction? selected = available.SingleOrDefault(item =>
            item.Id.Equals(requestedId.Value, StringComparison.Ordinal) &&
            item.Scope == requestedScope);
        if (selected is null)
        {
            return null;
        }

        Solution? firstSolution = await ChangedClosedSolutionAsync(
            baselineDocument,
            selected.Action,
            selected.Descriptor.AllowCrossDocument,
            cancellationToken);
        if (firstSolution is null || requestedScope is CodeIntelligenceCodeActionScope.Occurrence)
        {
            return firstSolution;
        }

        Document? first = firstSolution.GetDocument(baselineDocument.Id);
        if (first is null)
        {
            return null;
        }
        string previous = (await first.GetTextAsync(cancellationToken)).ToString();
        Document current = first;
        for (int applied = 1; applied < MaximumDocumentCodeActionApplications; applied++)
        {
            ClosedCodeAction? next = await FindMatchingDocumentActionAsync(
                current, selected, cancellationToken);
            if (next is null)
            {
                return current.Project.Solution;
            }

            Solution? changedSolution = await ChangedClosedSolutionAsync(
                current, next.Action, allowCrossDocument: false, cancellationToken);
            Document? changed = changedSolution?.GetDocument(current.Id);
            if (changed is null)
            {
                return null;
            }

            string candidate = (await changed.GetTextAsync(cancellationToken)).ToString();
            if (candidate.Equals(previous, StringComparison.Ordinal))
            {
                return null;
            }

            previous = candidate;
            current = changed;
        }

        return await FindMatchingDocumentActionAsync(current, selected, cancellationToken) is null
            ? current.Project.Solution
            : null;
    }

    private static async ValueTask<ClosedCodeAction?> FindMatchingDocumentActionAsync(
        Document document,
        ClosedCodeAction selected,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Diagnostic> diagnostics = await DocumentDiagnosticsAsync(
            document, cancellationToken);
        if (selected.Diagnostic is null || selected.Descriptor.FixProvider is null)
        {
            return null;
        }
        foreach (Diagnostic diagnostic in diagnostics
            .Where(item => item.Id.Equals(selected.Diagnostic.Id, StringComparison.Ordinal))
            .OrderBy(item => item.Location.SourceSpan.Start))
        {
            IReadOnlyList<CodeAction> actions = await RegisterActionsAsync(
                selected.Descriptor.FixProvider, document, diagnostic, cancellationToken);
            CodeAction? action = actions.FirstOrDefault(item => SameAction(item, selected.Action));
            if (action is not null)
            {
                return new(selected.Id, selected.Descriptor, diagnostic, action,
                    CodeIntelligenceCodeActionScope.Document,
                    diagnostic.Location.SourceSpan,
                    AffectedFileCount: 1,
                    ChangesActiveDocument: true);
            }
        }

        return null;
    }

    private static async ValueTask<IReadOnlyList<Diagnostic>> DocumentDiagnosticsAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        Compilation? compilation = await document.Project.GetCompilationAsync(cancellationToken);
        SyntaxTree? tree = await document.GetSyntaxTreeAsync(cancellationToken);
        if (compilation is null || tree is null)
        {
            return [];
        }

        ImmutableArray<DiagnosticAnalyzer> analyzers = document.Project.AnalyzerReferences
            .SelectMany(reference => reference.GetAnalyzers(document.Project.Language))
            .ToImmutableArray();
        ImmutableArray<Diagnostic> diagnostics = analyzers.IsEmpty
            ? compilation.GetDiagnostics(cancellationToken)
            : await compilation.WithAnalyzers(analyzers, document.Project.AnalyzerOptions)
                .GetAllDiagnosticsAsync(cancellationToken);
        return diagnostics
            .Where(item => item.Location.IsInSource && item.Location.SourceTree == tree)
            .Take(MaximumDiagnostics)
            .ToArray();
    }

    private static async ValueTask<IReadOnlyList<CodeAction>> RegisterActionsAsync(
        CodeFixProvider provider,
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        List<CodeAction> registered = [];
        CodeFixContext context = new(
            document,
            diagnostic,
            (action, _) => AddLeafActions(action, registered),
            cancellationToken);
        try
        {
            await provider.RegisterCodeFixesAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }

        return registered
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .GroupBy(item => (item.Title, item.EquivalenceKey))
            .Select(group => group.First())
            .Take(MaximumCodeActions)
            .ToArray();
    }

    private static async ValueTask<IReadOnlyList<CodeAction>> RegisterRefactoringsAsync(
        CodeRefactoringProvider provider,
        Document document,
        TextSpan span,
        CancellationToken cancellationToken)
    {
        List<CodeAction> registered = [];
        CodeRefactoringContext context = new(
            document,
            span,
            action => AddLeafActions(action, registered),
            cancellationToken);
        try
        {
            await provider.ComputeRefactoringsAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }

        return registered
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .GroupBy(item => (item.Title, item.EquivalenceKey))
            .Select(group => group.First())
            .Take(MaximumCodeActions)
            .ToArray();
    }

    private static void AddLeafActions(CodeAction action, List<CodeAction> actions)
    {
        if (!action.NestedActions.IsDefaultOrEmpty)
        {
            foreach (CodeAction nested in action.NestedActions)
            {
                AddLeafActions(nested, actions);
            }
            return;
        }

        actions.Add(action);
    }

    private static async ValueTask<Solution?> ChangedClosedSolutionAsync(
        Document document,
        CodeAction action,
        bool allowCrossDocument,
        CancellationToken cancellationToken)
    {
        Solution before = document.Project.Solution;
        ImmutableArray<CodeActionOperation> operations;
        try
        {
            operations = await action.GetOperationsAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return null;
        }
        ApplyChangesOperation? apply = operations.OfType<ApplyChangesOperation>().SingleOrDefault();
        if (operations.Length != 1 || apply is null)
        {
            return null;
        }

        Solution after = apply.ChangedSolution;
        SolutionChanges solutionChanges = after.GetChanges(before);
        ProjectChanges[] projects = solutionChanges.GetProjectChanges().ToArray();
        if (projects.Length is 0 or > MaximumCodeActions ||
            !projects.Any(project => project.ProjectId == document.Project.Id) ||
            solutionChanges.GetAddedProjects().Any() || solutionChanges.GetRemovedProjects().Any())
        {
            return null;
        }

        DocumentId[] changed = projects
            .SelectMany(project => project.GetChangedDocuments())
            .ToArray();
        bool sourceOnly = changed.Length is > 0 and <= MaximumCodeActions &&
            projects.All(changes =>
                !changes.GetAddedDocuments().Any() && !changes.GetRemovedDocuments().Any() &&
                !changes.GetAddedAdditionalDocuments().Any() &&
                !changes.GetRemovedAdditionalDocuments().Any() &&
                !changes.GetChangedAdditionalDocuments().Any() &&
                !changes.GetAddedAnalyzerConfigDocuments().Any() &&
                !changes.GetRemovedAnalyzerConfigDocuments().Any() &&
                !changes.GetChangedAnalyzerConfigDocuments().Any() &&
                !changes.GetAddedMetadataReferences().Any() &&
                !changes.GetRemovedMetadataReferences().Any() &&
                !changes.GetAddedProjectReferences().Any() &&
                !changes.GetRemovedProjectReferences().Any() &&
                !changes.GetAddedAnalyzerReferences().Any() &&
                !changes.GetRemovedAnalyzerReferences().Any());
        bool allowedScope = projects.Length == 1 && changed.Length == 1 &&
            changed[0] == document.Id || allowCrossDocument;
        return sourceOnly && allowedScope ? after : null;
    }

    private static bool SameAction(CodeAction candidate, CodeAction selected) =>
        !string.IsNullOrWhiteSpace(selected.EquivalenceKey)
            ? string.Equals(candidate.EquivalenceKey, selected.EquivalenceKey,
                StringComparison.Ordinal)
            : candidate.Title.Equals(selected.Title, StringComparison.Ordinal);

    private static int ChangedDocumentCount(Solution before, Solution after) =>
        after.GetChanges(before).GetProjectChanges()
            .SelectMany(project => project.GetChangedDocuments())
            .Select(document => before.GetDocument(document)?.FilePath ?? document.Id.ToString())
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static bool ChangesDocument(
        Solution before,
        Solution changed,
        DocumentId documentId) =>
        changed.GetChanges(before).GetProjectChanges()
            .SelectMany(project => project.GetChangedDocuments())
            .Contains(documentId);

    private static bool Touches(TextSpan span, int offset) =>
        span.Start <= offset && offset <= span.End;

    private static bool TryGetCodeActionSpan(
        SourceText text,
        int offset,
        CodeIntelligenceRange? range,
        out TextSpan span)
    {
        if (range is null)
        {
            span = new TextSpan(offset, 0);
            return true;
        }

        return TryGetTextSpan(text, range, out span);
    }

    private static string CodeActionId(
        ClosedCodeActionProvider provider,
        Diagnostic? diagnostic,
        CodeAction action,
        CodeIntelligenceCodeActionScope scope,
        TextSpan? refactoringSpan = null)
    {
        StringBuilder value = new();
        _ = value.Append(provider.Name).Append('\0')
            .Append(diagnostic?.Id).Append('\0')
            .Append(action.EquivalenceKey).Append('\0')
            .Append(action.Title).Append('\0')
            .Append(scope);
        if (scope is CodeIntelligenceCodeActionScope.Occurrence)
        {
            TextSpan span = diagnostic?.Location.SourceSpan ?? refactoringSpan ?? default;
            _ = value.Append('\0').Append(span.Start).Append(':').Append(span.Length);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    private static CodeIntelligenceCodeActionResult CodeActionFailure(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(
        snapshot.ContextId,
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        state,
        [],
        [Issue(code, message)]);

    private sealed record ClosedCodeAction(
        string Id,
        ClosedCodeActionProvider Descriptor,
        Diagnostic? Diagnostic,
        CodeAction Action,
        CodeIntelligenceCodeActionScope Scope,
        TextSpan ContextSpan,
        int AffectedFileCount,
        bool ChangesActiveDocument);

    private sealed record ClosedCodeActionProvider(
        string Name,
        CodeFixProvider? FixProvider,
        CodeRefactoringProvider? RefactoringProvider,
        CodeIntelligenceClosedCodeActionKind Kind,
        bool AllowDocumentScope,
        bool AllowCrossDocument);

    private sealed class ClosedCodeFixCatalog : IDisposable
    {
        private static readonly IReadOnlyDictionary<string, ClosedProviderPolicy> Policies =
            new Dictionary<string, ClosedProviderPolicy>(StringComparer.Ordinal)
            {
                ["CSharpImplementInterfaceCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.ImplementInterface, false),
                ["CSharpImplementAbstractClassCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.ImplementAbstractMembers, false),
                ["CSharpAddExplicitCastCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.AddExplicitCast, false),
                ["AssignOutParametersAboveReturnCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.AssignOutParameters, false),
                ["AssignOutParametersAtStartCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.AssignOutParameters, false),
                ["CSharpGenerateDefaultConstructorsCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.GenerateConstructor, false),
                ["CSharpGenerateVariableCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.GenerateVariable, false),
                ["CSharpAddParameterCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.AddParameter, false, true),
                ["CSharpFixReturnTypeCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.FixReturnType, false),
                ["CSharpMakeMemberStaticCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.MakeMemberStatic, false),
                ["CSharpMakeTypeAbstractCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.MakeTypeAbstract, false),
                ["CSharpMakeTypePartialCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.MakeTypePartial, true),
                ["CSharpRemoveUnnecessaryCastCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.RemoveUnnecessaryCast, true),
                ["SimplifyTypeNamesCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.SimplifyTypeName, true),
                ["CSharpUseNullPropagationCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.UseNullPropagation, true),
                ["CSharpUseCompoundAssignmentCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.UseCompoundAssignment, true),
                ["CSharpAddBracesCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.AddBraces, true),
                ["CSharpInlineDeclarationCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.InlineDeclaration, true),
                ["CSharpUseObjectInitializerCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.UseObjectInitializer, true),
                ["CSharpUseCollectionInitializerCodeFixProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.UseCollectionInitializer, true),
            };

        private static readonly IReadOnlyDictionary<string, ClosedProviderPolicy>
            RefactoringPolicies = new Dictionary<string, ClosedProviderPolicy>(StringComparer.Ordinal)
            {
                ["CSharpConvertAutoPropertyToFullPropertyCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.ConvertAutoPropertyToFullProperty, false),
                ["CSharpConvertForEachToForCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.ConvertLoop, false),
                ["CSharpConvertForToForEachCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.ConvertLoop, false),
                ["CSharpConvertIfToSwitchCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.ConvertIfToSwitch, false),
                ["CSharpConvertLocalFunctionToMethodCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.ConvertLocalFunctionToMethod, false),
                ["CSharpInlineTemporaryCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.InlineTemporary, false),
                ["CSharpIntroduceLocalForExpressionCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.IntroduceLocal, false),
                ["CSharpInvertConditionalCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.InvertConditional, false),
                ["CSharpInvertIfCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.InvertConditional, false),
                ["CSharpInvertLogicalCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.InvertConditional, false),
                ["CSharpMoveDeclarationNearReferenceCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.MoveDeclarationNearReference, false),
                ["ConvertNamespaceCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.ConvertNamespace, false),
                ["CSharpAddParameterCheckCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.AddParameterCheck, false),
                ["CSharpInitializeMemberFromParameterCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.InitializeMemberFromParameter, false),
                ["CSharpIntroduceUsingStatementCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.IntroduceUsingStatement, false),
                ["UseExplicitTypeCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.UseExplicitType, false),
                ["UseImplicitTypeCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.UseImplicitType, false),
                ["UseExpressionBodyCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.UseExpressionBody, false),
                ["UseExpressionBodyForLambdaCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.UseExpressionBody, false),
                ["ExtractMethodCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.ExtractMethod, false),
                ["IntroduceVariableCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.IntroduceVariable, false),
                ["GenerateEqualsAndGetHashCodeFromMembersCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.GenerateEqualityMembers, false),
                ["GenerateOverridesCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.GenerateOverrides, false),
                ["ReplaceMethodWithPropertyCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.ReplaceMemberKind, false, true),
                ["ReplacePropertyWithMethodsCodeRefactoringProvider"] = new(
                    CodeIntelligenceClosedCodeActionKind.ReplaceMemberKind, false, true),
            };

        private readonly CompositionHost composition;
        private readonly IReadOnlyList<ClosedCodeActionProvider> providers;

        internal ClosedCodeFixCatalog()
        {
            composition = new ContainerConfiguration()
                .WithAssemblies(MefHostServices.DefaultAssemblies)
                .CreateContainer();
            CodeFixProvider[] exports = composition.GetExports<CodeFixProvider>().ToArray();
            CodeRefactoringProvider[] refactoringExports =
                composition.GetExports<CodeRefactoringProvider>().ToArray();
            ProviderNames = exports
                .Select(provider => provider.GetType().Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            providers = exports
                .Select(provider => (Provider: provider,
                    Policy: Policies.GetValueOrDefault(provider.GetType().Name)))
                .Where(item => item.Policy is not null)
                .Select(item => new ClosedCodeActionProvider(
                    item.Provider.GetType().Name, item.Provider, RefactoringProvider: null,
                    item.Policy!.Kind, item.Policy.AllowDocumentScope,
                    item.Policy.AllowCrossDocument))
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ToArray();
            RefactoringProviderNames = refactoringExports
                .Select(provider => provider.GetType().Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            RefactoringProviders = refactoringExports
                .Select(provider => (Provider: provider,
                    Policy: RefactoringPolicies.GetValueOrDefault(provider.GetType().Name)))
                .Where(item => item.Policy is not null)
                .Select(item => new ClosedCodeActionProvider(
                    item.Provider.GetType().Name, FixProvider: null, item.Provider,
                    item.Policy!.Kind, AllowDocumentScope: false,
                    item.Policy.AllowCrossDocument))
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ToArray();
        }

        internal IReadOnlyList<string> ProviderNames { get; }

        internal IReadOnlyList<string> RefactoringProviderNames { get; }

        internal IReadOnlyList<ClosedCodeActionProvider> RefactoringProviders { get; }

        internal IEnumerable<ClosedCodeActionProvider> ProvidersFor(string diagnosticId) =>
            providers.Where(item => item.FixProvider!.FixableDiagnosticIds.Contains(
                diagnosticId, StringComparer.Ordinal));

        public void Dispose() => composition.Dispose();
    }

    private sealed record ClosedProviderPolicy(
        CodeIntelligenceClosedCodeActionKind Kind,
        bool AllowDocumentScope,
        bool AllowCrossDocument = false);
}
