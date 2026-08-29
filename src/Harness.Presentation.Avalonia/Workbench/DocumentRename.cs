using Avalonia.Controls;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class DocumentRename
{
    private readonly IWorkspaceMutationService? mutations;
    private readonly Func<SourceDocumentSession?> activeSession;
    private readonly Func<IReadOnlyDictionary<string, SourceDocumentSession>> documents;
    private readonly Func<Window?> ownerWindow;
    private readonly Func<ValueTask> invalidate;
    private readonly Action<SourceDocumentSession, bool> scheduleDiagnostics;
    private readonly CancellationToken cancellationToken;

    internal DocumentRename(
        IWorkspaceMutationService? mutations,
        Func<SourceDocumentSession?> activeSession,
        Func<IReadOnlyDictionary<string, SourceDocumentSession>> documents,
        Func<Window?> ownerWindow,
        Func<ValueTask> invalidate,
        Action<SourceDocumentSession, bool> scheduleDiagnostics,
        CancellationToken cancellationToken)
    {
        this.mutations = mutations;
        this.activeSession = activeSession;
        this.documents = documents;
        this.ownerWindow = ownerWindow;
        this.invalidate = invalidate;
        this.scheduleDiagnostics = scheduleDiagnostics;
        this.cancellationToken = cancellationToken;
    }

    internal async ValueTask RenameAsync(SourceDocumentSession document)
    {
        if (mutations is null || document.View.GoalId is null ||
            document.View.Access is not WorkbenchDocumentAccess.Editable ||
            !DocumentIntelligence.CanUse(document) || ownerWindow() is not { } owner)
        {
            document.SetStatus("Semantic rename requires an editable approved goal source document.");
            return;
        }
        RenameNameDialog name = new();
        await name.ShowDialog(owner);
        if (name.Result is not { } newName) return;
        PendingWorkbenchRename? pending = await PreviewActiveAsync(newName);
        if (pending is null) return;
        RenamePreviewDialog preview = new(pending.Preview);
        if (!await preview.ShowDialog<bool>(owner))
        {
            document.SetStatus("Rename preview closed without changing files.");
            return;
        }
        _ = await ApplyActiveAsync(pending);
    }

    internal async ValueTask<PendingWorkbenchRename?> PreviewActiveAsync(string newName)
    {
        SourceDocumentSession? document = activeSession();
        if (mutations is null || document?.View.GoalId is null || document.View.Sha256 is null ||
            document.View.Access is not WorkbenchDocumentAccess.Editable || document.View.IsTruncated)
            return null;
        WorkbenchCodeBufferVersion version = new(Math.Max(1, document.CurrentBufferVersion));
        RenameSymbolPreviewRequest request = new(
            document.View.GoalId.Value,
            new(document.View.Path.Value),
            new(document.View.Sha256.Value),
            version,
            new(document.Editor.Text),
            document.Editor.CaretPosition,
            new(newName),
            RenameSymbolOrigin.Human,
            []);
        document.SetBusy(true, "Resolving rename with Roslyn…");
        try
        {
            RenameSymbolPreviewView result = await mutations.PreviewRenameAsync(
                request, cancellationToken);
            if (result.Preview is null || result.ErrorCode is not null)
            {
                document.SetStatus(result.Error ?? "Rename preview is unavailable.");
                return null;
            }
            document.SetStatus(result.Preview.Disposition is
                WorkbenchCodeTransformationDisposition.Ready && result.Preview.Fingerprint is not null
                ? $"Rename preview ready · {result.Preview.Edits.Count} affected file(s)."
                : result.Preview.Conflicts.FirstOrDefault()?.Message.Value ??
                  result.Preview.Issues.FirstOrDefault()?.Message.Value ??
                  "Rename has conflicts and cannot be applied.");
            return new(request, result.Preview);
        }
        catch (OperationCanceledException)
        {
            document.SetStatus("Rename preview cancelled.");
            return null;
        }
        finally
        {
            document.SetBusy(false);
            await invalidate();
        }
    }

    internal async ValueTask<RenameSymbolApplyView?> ApplyActiveAsync(PendingWorkbenchRename pending)
    {
        SourceDocumentSession? active = activeSession();
        if (mutations is null || pending.Preview.Fingerprint is null || active is null) return null;
        active.SetBusy(true, "Applying the accepted rename atomically…");
        try
        {
            RenameSymbolApplyView result = await mutations.ApplyRenameAsync(new(
                pending.Request, NewEditCorrelation(), pending.Preview.Fingerprint), cancellationToken);
            if (result.ErrorCode is not null)
            {
                active.SetStatus(result.Error ?? "Rename was not applied.");
                return result;
            }
            foreach (WorkbenchCodeRenameEdit edit in result.Preview!.Edits)
            {
                SourceDocumentSession? open = documents().Values.FirstOrDefault(candidate =>
                    candidate.View.Path.Value.Equals(edit.Path.Value, StringComparison.Ordinal));
                FileEditView? evidence = result.Files.FirstOrDefault(file =>
                    file.Path.Equals(edit.Path.Value, StringComparison.Ordinal));
                if (open is null || evidence?.NewSha256 is null) continue;
                open.ReplaceWith(open.View with
                {
                    Content = new(edit.Text.Value),
                    Sha256 = new(evidence.NewSha256),
                    Size = new(evidence.BytesWritten),
                });
                open.SetStatus(
                    $"Renamed to {pending.Preview.NewName.Value} · compiler-verified atomic apply.");
            }
            await invalidate();
            scheduleDiagnostics(active, true);
            return result;
        }
        catch (OperationCanceledException)
        {
            active.SetStatus("Rename cancelled; no partial file set was accepted.");
            return null;
        }
        finally
        {
            active.SetBusy(false);
        }
    }

    private static ToolCorrelationId NewEditCorrelation() =>
        new($"desktop-edit-{Guid.NewGuid():N}");
}
