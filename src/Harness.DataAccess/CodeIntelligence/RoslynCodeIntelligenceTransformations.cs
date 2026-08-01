using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private const int MaximumRenameFiles = 100;
    private const int MaximumRenamePreviewBytes = 10 * 1024 * 1024;

    public async ValueTask<CodeIntelligenceRenamePreviewResult> PreviewRenameAsync(
        CodeIntelligenceRenamePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        CodeIntelligenceInteractiveSnapshot snapshot = request.Snapshot;
        ActiveSession? session = MatchingSession(snapshot);
        if (session is null)
        {
            return RenameFailure(request, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        if (session.SourceKind is not CodeIntelligenceSourceKind.ApprovedGoalWorktree)
        {
            return RenameFailure(request, CodeIntelligenceResultState.Failed,
                "editable_context_required", "Rename requires an approved goal worktree context.");
        }

        if (!ValidIdentifier(request.NewName.Value))
        {
            return RenameFailure(request, CodeIntelligenceResultState.Failed,
                "invalid_identifier", "The new name must be a valid non-keyword C# identifier.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(session, snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return RenameFailure(request, prepared.State, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value);
            }

            ISymbol? symbol = await SymbolFinder.FindSymbolAtPositionAsync(
                prepared.Document!, prepared.Offset, cancellationToken);
            if (symbol is null || symbol.Kind is SymbolKind.Namespace or SymbolKind.Assembly or SymbolKind.NetModule)
            {
                return RenameFailure(request, CodeIntelligenceResultState.Failed,
                    "symbol_not_renameable", "No renameable source symbol is available at the active caret.");
            }

            string symbolIdentity = SymbolIdentity(symbol, session.RootPath);
            if (!symbol.Locations.Any(location => location.IsInSource))
            {
                return RenameConflictResult(
                    request,
                    session,
                    symbolIdentity,
                    Conflict(CodeIntelligenceRenameConflictKind.Metadata,
                        "Metadata symbols cannot be renamed in the approved source context.", null));
            }

            Solution baseline = prepared.Document!.Project.Solution;
            SymbolRenameOptions options = new(
                RenameOverloads: false,
                RenameInStrings: false,
                RenameInComments: false,
                RenameFile: false);
#pragma warning disable CS0618
            Solution candidate = await Renamer.RenameSymbolAsync(
                baseline, symbol, options, request.NewName.Value, cancellationToken);
#pragma warning restore CS0618
            SolutionChanges changes = candidate.GetChanges(baseline);
            IReadOnlyList<DocumentId> changedDocuments = changes.GetProjectChanges()
                .SelectMany(change => change.GetChangedDocuments())
                .Distinct()
                .ToArray();
            if (changedDocuments.Count == 0)
            {
                return RenameFailure(request, CodeIntelligenceResultState.Failed,
                    "rename_produced_no_changes", "Roslyn did not produce any source changes for this symbol.");
            }

            List<CodeIntelligenceRenameConflict> conflicts = [];
            Dictionary<string, RenameDocumentCandidate> byPath = new(StringComparer.Ordinal);
            foreach (DocumentId documentId in changedDocuments)
            {
                Document? oldDocument = baseline.GetDocument(documentId);
                Document? newDocument = candidate.GetDocument(documentId);
                if (oldDocument?.FilePath is null || newDocument is null)
                {
                    conflicts.Add(Conflict(CodeIntelligenceRenameConflictKind.Generated,
                        "A generated document cannot be changed atomically.", null));
                    continue;
                }

                string fullPath = Path.GetFullPath(oldDocument.FilePath);
                string relative = Path.GetRelativePath(session.RootPath, fullPath).Replace('\\', '/');
                if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal))
                {
                    conflicts.Add(Conflict(CodeIntelligenceRenameConflictKind.OutsideSourceContext,
                        "A rename location is outside the approved source context.", null));
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    conflicts.Add(Conflict(CodeIntelligenceRenameConflictKind.Generated,
                        "A generated or missing source document is not editable.", new(relative)));
                    continue;
                }

                if (!IsWritable(fullPath))
                {
                    conflicts.Add(Conflict(CodeIntelligenceRenameConflictKind.Uneditable,
                        "A source document affected by the rename is not writable.", new(relative)));
                    continue;
                }

                SourceText oldText = await oldDocument.GetTextAsync(cancellationToken);
                SourceText newText = await newDocument.GetTextAsync(cancellationToken);
                string content = newText.ToString();
                string persisted = await File.ReadAllTextAsync(fullPath, Utf8WithoutBom, cancellationToken);
                RenameDocumentCandidate item = new(
                    new(relative),
                    new(Hash(persisted)),
                    new(persisted),
                    new(content),
                    newText.GetTextChanges(oldText).Count);
                if (byPath.TryGetValue(relative, out RenameDocumentCandidate? linked) &&
                    !linked.Text.Value.Equals(content, StringComparison.Ordinal))
                {
                    conflicts.Add(Conflict(CodeIntelligenceRenameConflictKind.InconsistentLinkedFile,
                        "Linked documents produced different edits for the same physical file.", new(relative)));
                    continue;
                }

                byPath[relative] = item;
            }

            if (byPath.Count > MaximumRenameFiles)
            {
                conflicts.Add(Conflict(CodeIntelligenceRenameConflictKind.TooManyFiles,
                    $"Rename affects more than the supported {MaximumRenameFiles} files.", null));
            }
            if (byPath.Values.Sum(item =>
                    (long)Utf8WithoutBom.GetByteCount(item.OriginalText.Value) +
                    Utf8WithoutBom.GetByteCount(item.Text.Value)) > MaximumRenamePreviewBytes)
            {
                conflicts.Add(Conflict(CodeIntelligenceRenameConflictKind.TooLarge,
                    "The complete rename diff exceeds the 10 MiB preview evidence limit.", null));
            }

            HashSet<ProjectId> affectedProjects = changedDocuments.Select(id => id.ProjectId).ToHashSet();
            IReadOnlyList<CollectedDiagnostic> baselineDiagnostics =
                await CollectDiagnosticsAsync(baseline, affectedProjects, session.RootPath, cancellationToken);
            IReadOnlyList<CollectedDiagnostic> candidateDiagnostics =
                await CollectDiagnosticsAsync(candidate, affectedProjects, session.RootPath, cancellationToken);
            IReadOnlyList<CodeIntelligenceValidationDiagnostic> delta = CompareDiagnostics(
                baselineDiagnostics, candidateDiagnostics);
            foreach (CodeIntelligenceValidationDiagnostic diagnostic in delta.Where(item =>
                         item.Kind is CodeIntelligenceDiagnosticDeltaKind.Introduced &&
                         item.Diagnostic.Severity is CodeIntelligenceDiagnosticSeverity.Error))
            {
                conflicts.Add(Conflict(
                    CodeIntelligenceRenameConflictKind.Semantic,
                    $"{diagnostic.Diagnostic.Id.Value}: {diagnostic.Diagnostic.Message.Value}",
                    diagnostic.Diagnostic.Path));
            }

            IReadOnlyList<CodeIntelligenceRenameEdit> edits = byPath.Values
                .OrderBy(item => item.Path.Value, StringComparer.Ordinal)
                .Select(item => new CodeIntelligenceRenameEdit(
                    item.Path, item.BaselineHash, item.OriginalText, item.Text,
                    item.ReplacementCount))
                .ToArray();
            CodeIntelligenceTransformationDisposition disposition = conflicts.Count == 0
                ? CodeIntelligenceTransformationDisposition.Ready
                : CodeIntelligenceTransformationDisposition.Conflicted;
            CodeIntelligenceTransformationFingerprint? fingerprint = disposition is
                CodeIntelligenceTransformationDisposition.Ready
                    ? new(Fingerprint(snapshot, symbolIdentity, request.NewName.Value, edits, delta))
                    : null;
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                disposition,
                new(symbolIdentity),
                request.NewName,
                edits,
                conflicts.Take(MaximumIssues).ToArray(),
                delta.Take(MaximumDiagnostics).ToArray(),
                fingerprint,
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException)
        {
            return RenameFailure(request, CodeIntelligenceResultState.Failed,
                "rename_preview_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    private static bool ValidIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
        SyntaxFacts.IsValidIdentifier(value) &&
        SyntaxFacts.GetKeywordKind(value) is SyntaxKind.None;

    private static bool IsWritable(string path)
    {
        FileInfo file = new(path);
        if (file.IsReadOnly)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        UnixFileMode mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0;
    }

    private static string SymbolIdentity(ISymbol symbol, string root)
    {
        string documentationId = symbol.GetDocumentationCommentId() ?? string.Empty;
        Location? source = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        FileLinePositionSpan? span = source?.GetLineSpan();
        string sourcePath = span?.Path is { Length: > 0 } path
            ? Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/')
            : string.Empty;
        if (sourcePath == ".." || sourcePath.StartsWith("../", StringComparison.Ordinal))
        {
            sourcePath = string.Empty;
        }

        return string.Join('|',
            symbol.Kind,
            documentationId,
            symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sourcePath,
            span?.StartLinePosition.Line.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            span?.StartLinePosition.Character.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static string Fingerprint(
        CodeIntelligenceInteractiveSnapshot snapshot,
        string symbol,
        string newName,
        IReadOnlyList<CodeIntelligenceRenameEdit> edits,
        IReadOnlyList<CodeIntelligenceValidationDiagnostic> diagnostics)
    {
        StringBuilder value = new();
        _ = value.Append(snapshot.ContextId.Value).Append('\n')
            .Append(snapshot.Path.Value).Append('\n')
            .Append(snapshot.BaselineHash.Value).Append('\n')
            .Append(snapshot.BufferVersion.Value).Append('\n')
            .Append(symbol).Append('\n').Append(newName).Append('\n');
        foreach (CodeIntelligenceRenameEdit edit in edits)
        {
            _ = value.Append(edit.Path.Value).Append('\0')
                .Append(edit.BaselineHash.Value).Append('\0')
                .Append(Hash(edit.Text.Value)).Append('\0')
                .Append(edit.ReplacementCount).Append('\n');
        }

        foreach (CodeIntelligenceValidationDiagnostic item in diagnostics)
        {
            _ = value.Append(item.Kind).Append('\0')
                .Append(item.Diagnostic.Id.Value).Append('\0')
                .Append(item.Diagnostic.Path.Value).Append('\0')
                .Append(item.Diagnostic.Message.Value).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    private static CodeIntelligenceRenameConflict Conflict(
        CodeIntelligenceRenameConflictKind kind,
        string message,
        CodeIntelligenceDocumentPath? path) => new(kind, new(Bound(message, MaximumIssueLength)), path);

    private static CodeIntelligenceRenamePreviewResult RenameFailure(
        CodeIntelligenceRenamePreviewRequest request,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(
        request.Snapshot.ContextId,
        request.Snapshot.SessionId,
        request.Snapshot.Path,
        request.Snapshot.BufferVersion,
        state,
        CodeIntelligenceTransformationDisposition.Rejected,
        Symbol: null,
        request.NewName,
        [],
        [],
        [],
        Fingerprint: null,
        [Issue(code, message)]);

    private static CodeIntelligenceRenamePreviewResult RenameConflictResult(
        CodeIntelligenceRenamePreviewRequest request,
        ActiveSession session,
        string symbol,
        CodeIntelligenceRenameConflict conflict) => new(
        request.Snapshot.ContextId,
        request.Snapshot.SessionId,
        request.Snapshot.Path,
        request.Snapshot.BufferVersion,
        SessionState(session),
        CodeIntelligenceTransformationDisposition.Conflicted,
        new(symbol),
        request.NewName,
        [],
        [conflict],
        [],
        Fingerprint: null,
        session.Issues.ToArray());

    private sealed record RenameDocumentCandidate(
        CodeIntelligenceDocumentPath Path,
        CodeIntelligenceBaselineHash BaselineHash,
        CodeIntelligenceText OriginalText,
        CodeIntelligenceText Text,
        int ReplacementCount);
}
