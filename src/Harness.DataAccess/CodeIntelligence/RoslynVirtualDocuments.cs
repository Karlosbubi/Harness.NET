using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private const int MaximumVirtualDocuments = 500;
    private const int MaximumVirtualDocumentCharacters = 2 * 1024 * 1024;

    public async ValueTask<CodeIntelligenceVirtualDocumentResult> GetVirtualDocumentAsync(
        CodeIntelligenceVirtualDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ActiveSession? session = MatchingSession(request.Snapshot);
        if (session is null)
            return VirtualFailure(request, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, request.Snapshot, cancellationToken);
            if (prepared.Issue is not null)
                return VirtualFailure(request, prepared.State, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value);
            if (!session.VirtualDocuments.TryGetValue(request.Id.Value, out VirtualDocumentTarget? target) ||
                target.SourcePath != request.Snapshot.Path.Value ||
                target.BufferVersion != request.Snapshot.BufferVersion.Value ||
                target.SourceTextHash != Hash(request.Snapshot.Text.Value))
            {
                return VirtualFailure(request, CodeIntelligenceResultState.Stale,
                    "virtual_document_stale",
                    "The virtual source handle does not match the current document buffer.");
            }

            Project? project = prepared.Document!.Project.Solution.GetProject(target.ProjectId);
            if (project is null)
                return VirtualFailure(request, CodeIntelligenceResultState.Stale,
                    "virtual_project_stale", "The originating project is no longer available.");
            CodeIntelligenceVirtualDocumentOrigin? origin = await OriginAsync(
                project, request.Snapshot, cancellationToken);
            if (origin is null)
                return VirtualFailure(request, CodeIntelligenceResultState.Degraded,
                    "compilation_unavailable", "The originating project did not produce a compilation.");

            return target.Kind switch
            {
                CodeIntelligenceVirtualDocumentKind.GeneratedSource =>
                    await GeneratedDocumentAsync(request, project, target, origin, cancellationToken),
                CodeIntelligenceVirtualDocumentKind.MetadataSignature =>
                    await MetadataDocumentAsync(request, project, target, origin, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(target.Kind)),
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return VirtualFailure(request, CodeIntelligenceResultState.Failed,
                "virtual_document_failed", exception.Message);
        }
        finally { session.OperationGate.Release(); }
    }

    private async ValueTask<CodeIntelligenceSymbolDestination> MapNavigableDestinationAsync(
        ActiveSession session,
        CodeIntelligenceInteractiveSnapshot snapshot,
        Project sourceProject,
        ISymbol symbol,
        Location location,
        CancellationToken cancellationToken)
    {
        CodeIntelligenceSymbolDestination mapped = MapDestination(
            location,
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            session.RootPath);
        if (mapped.Kind is CodeIntelligenceDestinationKind.Source)
            return mapped;

        if (mapped.Kind is CodeIntelligenceDestinationKind.Generated && location.SourceTree is not null)
        {
            foreach (Project project in sourceProject.Solution.Projects)
            {
                SourceGeneratedDocument[] generated =
                    (await project.GetSourceGeneratedDocumentsAsync(cancellationToken)).ToArray();
                SourceGeneratedDocument? document = null;
                foreach (SourceGeneratedDocument candidate in generated)
                {
                    SyntaxTree? tree = await candidate.GetSyntaxTreeAsync(cancellationToken);
                    if (ReferenceEquals(tree, location.SourceTree))
                    {
                        document = candidate;
                        break;
                    }
                }
                if (document is null) continue;
                CodeIntelligenceVirtualDocumentId id = RegisterVirtualDocument(
                    session, snapshot, new(
                        CodeIntelligenceVirtualDocumentKind.GeneratedSource,
                        snapshot.Path.Value,
                        snapshot.BufferVersion.Value,
                        Hash(snapshot.Text.Value),
                        project.Id,
                        document.Id,
                        symbol,
                        location.SourceSpan,
                        document.Name));
                return mapped with { VirtualDocumentId = id };
            }
        }

        CodeIntelligenceVirtualDocumentId metadataId = RegisterVirtualDocument(
            session, snapshot, new(
                CodeIntelligenceVirtualDocumentKind.MetadataSignature,
                snapshot.Path.Value,
                snapshot.BufferVersion.Value,
                Hash(snapshot.Text.Value),
                sourceProject.Id,
                DocumentId: null,
                symbol,
                SourceSpan: null,
                $"{symbol.ContainingType?.Name ?? symbol.Name} · metadata"));
        return new(
            CodeIntelligenceDestinationKind.Metadata,
            mapped.Display,
            Path: null,
            mapped.Range,
            metadataId);
    }

    private static CodeIntelligenceVirtualDocumentId RegisterVirtualDocument(
        ActiveSession session,
        CodeIntelligenceInteractiveSnapshot snapshot,
        VirtualDocumentTarget target)
    {
        string key = Hash(string.Join('\n',
            session.SessionId.Value,
            snapshot.Path.Value,
            snapshot.BufferVersion.Value,
            target.Kind,
            target.ProjectId.Id,
            target.DocumentId?.Id,
            target.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            target.SourceSpan?.Start,
            target.SourceSpan?.Length,
            target.SourceTextHash));
        if (!session.VirtualDocuments.ContainsKey(key))
        {
            while (session.VirtualDocumentOrder.Count >= MaximumVirtualDocuments)
                session.VirtualDocuments.Remove(session.VirtualDocumentOrder.Dequeue());
            session.VirtualDocuments.Add(key, target);
            session.VirtualDocumentOrder.Enqueue(key);
        }
        return new(key);
    }

    private static async ValueTask<CodeIntelligenceVirtualDocumentResult> GeneratedDocumentAsync(
        CodeIntelligenceVirtualDocumentRequest request,
        Project project,
        VirtualDocumentTarget target,
        CodeIntelligenceVirtualDocumentOrigin origin,
        CancellationToken cancellationToken)
    {
        SourceGeneratedDocument[] generated =
            (await project.GetSourceGeneratedDocumentsAsync(cancellationToken)).ToArray();
        SourceGeneratedDocument? document = generated.FirstOrDefault(item => item.Id == target.DocumentId);
        if (document is null)
            return VirtualFailure(request, CodeIntelligenceResultState.Stale,
                "generated_document_stale", "The source generator no longer returns this document.");
        SourceText text = await document.GetTextAsync(cancellationToken);
        if (text.Length > MaximumVirtualDocumentCharacters)
            return VirtualFailure(request, CodeIntelligenceResultState.Degraded,
                "virtual_document_too_large", "The generated document exceeds the 2 MiB character limit.");
        CodeIntelligenceRange? selection = target.SourceSpan is { } span && span.End <= text.Length
            ? Range(text, span) : null;
        return new(request.Snapshot.ContextId, request.Snapshot.SessionId,
            request.Snapshot.Path, request.Snapshot.BufferVersion, CodeIntelligenceResultState.Ready,
            request.Id, target.Kind, new(target.Title), new(text.ToString()), selection, origin,
            IsReadOnly: true, []);
    }

    private static async ValueTask<CodeIntelligenceVirtualDocumentResult> MetadataDocumentAsync(
        CodeIntelligenceVirtualDocumentRequest request,
        Project project,
        VirtualDocumentTarget target,
        CodeIntelligenceVirtualDocumentOrigin origin,
        CancellationToken cancellationToken)
    {
        MetadataDecompilation? decompiled = await TryDecompileMetadataAsync(
            project, target.Symbol, origin, cancellationToken);
        if (decompiled is not null)
        {
            SourceText decompiledText = SourceText.From(decompiled.Text, Encoding.UTF8);
            int selectedOffset = decompiled.Text.IndexOf(target.Symbol.Name, StringComparison.Ordinal);
            CodeIntelligenceRange? decompiledSelection = selectedOffset >= 0
                ? Range(decompiledText, new TextSpan(selectedOffset, target.Symbol.Name.Length))
                : null;
            return new(request.Snapshot.ContextId, request.Snapshot.SessionId,
                request.Snapshot.Path, request.Snapshot.BufferVersion,
                CodeIntelligenceResultState.Ready, request.Id,
                CodeIntelligenceVirtualDocumentKind.DecompiledSource,
                new(target.Title.Replace(" · metadata", " · decompiled", StringComparison.Ordinal)),
                new(decompiled.Text), decompiledSelection, origin, IsReadOnly: true, []);
        }

        SyntaxGenerator generator = SyntaxGenerator.GetGenerator(project);
        ISymbol selected = target.Symbol;
        INamedTypeSymbol? type = selected as INamedTypeSymbol ?? selected.ContainingType;
        if (type is null)
            return VirtualFailure(request, CodeIntelligenceResultState.Degraded,
                "metadata_type_unavailable", "The metadata symbol has no containing type.");

        SyntaxNode declaration = generator.Declaration(type);
        List<SyntaxNode> members = [];
        foreach (ISymbol member in type.GetMembers().Where(IsVisibleMetadataMember)
                     .OrderBy(item => item.Kind).ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            try { members.Add(generator.Declaration(member)); }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or NotSupportedException)
            { }
        }
        if (members.Count > 0) declaration = generator.AddMembers(declaration, members);
        if (!type.ContainingNamespace.IsGlobalNamespace)
            declaration = generator.NamespaceDeclaration(
                type.ContainingNamespace.ToDisplayString(), declaration);
        declaration = generator.CompilationUnit(declaration);

        string header = $"// Metadata signature generated locally by Harness.NET.\n" +
                        $"// Assembly: {origin.Assembly.Value}\n" +
                        $"// Project: {origin.Project.Value} · {origin.TargetFramework.Value} · {origin.Configuration.Value}\n" +
                        "// Read-only. Method bodies are not decompiled.\n\n";
        string content = header + declaration.NormalizeWhitespace().ToFullString() + "\n";
        if (content.Length > MaximumVirtualDocumentCharacters)
            return VirtualFailure(request, CodeIntelligenceResultState.Degraded,
                "virtual_document_too_large", "The metadata signature exceeds the 2 MiB character limit.");
        SourceText text = SourceText.From(content, Encoding.UTF8);
        int offset = content.IndexOf(selected.Name, StringComparison.Ordinal);
        CodeIntelligenceRange? selection = offset >= 0
            ? Range(text, new TextSpan(offset, selected.Name.Length)) : null;
        return new(request.Snapshot.ContextId, request.Snapshot.SessionId,
            request.Snapshot.Path, request.Snapshot.BufferVersion, CodeIntelligenceResultState.Ready,
            request.Id, target.Kind, new(target.Title), new(content), selection, origin,
            IsReadOnly: true,
            [new(new("decompilation_unavailable"), new(
                "No local implementation body was available; showing metadata signatures instead."))]);
    }

    private static bool IsVisibleMetadataMember(ISymbol symbol) =>
        !symbol.IsImplicitlyDeclared &&
        symbol.Kind is not SymbolKind.NamedType &&
        symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or
            Accessibility.ProtectedOrInternal;

    private static async ValueTask<CodeIntelligenceVirtualDocumentOrigin?> OriginAsync(
        Project project,
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        Compilation? compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation is null) return null;
        AnalyzerConfigOptions options = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        _ = options.TryGetValue("build_property.TargetFramework", out string? framework);
        _ = options.TryGetValue("build_property.Configuration", out string? configuration);
        string assembly = compilation.Assembly.Identity.GetDisplayName();
        string identity = Hash(string.Join('\n', project.Id.Id, project.Version,
            assembly, framework, configuration, snapshot.Path.Value,
            snapshot.BufferVersion.Value, Hash(snapshot.Text.Value)));
        return new(new(project.Name), new(project.Version.ToString()),
            new(string.IsNullOrWhiteSpace(framework) ? "unknown" : framework),
            new(string.IsNullOrWhiteSpace(configuration) ? "unknown" : configuration),
            new(assembly), new(identity));
    }

    private static CodeIntelligenceVirtualDocumentResult VirtualFailure(
        CodeIntelligenceVirtualDocumentRequest request,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(
            request.Snapshot.ContextId, request.Snapshot.SessionId, request.Snapshot.Path,
            request.Snapshot.BufferVersion, state, request.Id, Kind: null, Title: null, Text: null,
            SelectionRange: null, Origin: null, IsReadOnly: true,
            [new(new(code), new(Bound(message, MaximumIssueLength)))]);

    private sealed record VirtualDocumentTarget(
        CodeIntelligenceVirtualDocumentKind Kind,
        string SourcePath,
        long BufferVersion,
        string SourceTextHash,
        ProjectId ProjectId,
        DocumentId? DocumentId,
        ISymbol Symbol,
        TextSpan? SourceSpan,
        string Title);
}
