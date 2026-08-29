using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class DocumentInteractions
{
    private readonly IWorkbenchCodeIntelligenceService service;
    private readonly DocumentIntelligence intelligence;
    private readonly Func<Window?> ownerWindow;
    private readonly Func<WorkbenchCodeSymbolDestination, GoalId?, ValueTask> navigate;
    private readonly CancellationToken cancellationToken;

    internal DocumentInteractions(
        IWorkbenchCodeIntelligenceService service,
        DocumentIntelligence intelligence,
        Func<Window?> ownerWindow,
        Func<WorkbenchCodeSymbolDestination, GoalId?, ValueTask> navigate,
        CancellationToken cancellationToken)
    {
        this.service = service;
        this.intelligence = intelligence;
        this.ownerWindow = ownerWindow;
        this.navigate = navigate;
        this.cancellationToken = cancellationToken;
    }

    internal async ValueTask ShowWorkspaceSymbolsAsync(SourceDocumentSession document)
    {
        if (!DocumentIntelligence.CanUse(document) || ownerWindow() is not { } owner) return;
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            document.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? session = await intelligence.EnsureSessionAsync(document, token);
            if (session is null || !document.IsCurrentInteraction(version)) return;
            WorkbenchCodeInteractiveSnapshot snapshot = DocumentIntelligence.Snapshot(
                document, session, version);
            WorkspaceSymbolSearchDialog dialog = new(
                async (value, searchCancellation) =>
                {
                    using CancellationTokenSource linked =
                        CancellationTokenSource.CreateLinkedTokenSource(token, searchCancellation);
                    return await service.SearchSymbolsAsync(
                        new(snapshot, value, MaximumResults: 200, Offset: 0), linked.Token);
                },
                destination => navigate(destination, document.View.GoalId));
            await dialog.ShowDialog(owner);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    internal async ValueTask ShowCompletionAsync(
        SourceDocumentSession document,
        WorkbenchCodeCompletionTriggerKind triggerKind,
        char? triggerCharacter)
    {
        if (!DocumentIntelligence.CanUse(document)) return;
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            document.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? session = await intelligence.EnsureSessionAsync(document, token);
            if (session is null || !document.IsCurrentInteraction(version)) return;
            WorkbenchCodeInteractiveSnapshot snapshot = DocumentIntelligence.Snapshot(
                document, session, version);
            WorkbenchCodeCompletionView result = await service.GetCompletionsAsync(
                new(snapshot, triggerKind, triggerCharacter), token);
            if (!document.IsCurrentInteraction(version) || result.ListId is null ||
                result.Items.Count == 0 || result.State is WorkbenchCodeResultState.Stale or
                    WorkbenchCodeResultState.Cancelled or WorkbenchCodeResultState.Failed) return;
            document.CompletionWindow?.Hide();
            CompletionWindow window = new RoslynCompletionWindow(document.NativeEditor.TextArea)
            {
                StartOffset = document.Editor.GetOffset(result.ApplicableRange.Start),
                EndOffset = document.Editor.GetOffset(result.ApplicableRange.End),
                CloseWhenCaretAtBeginning = triggerKind is WorkbenchCodeCompletionTriggerKind.Invoke,
            };
            foreach (WorkbenchCodeCompletionItem item in result.Items)
                window.CompletionList.CompletionData.Add(new RoslynCompletionData(
                    item,
                    (selected, commitCharacter) => _ = CommitCompletionAsync(
                        document, snapshot, result.ListId, selected, commitCharacter)));
            AutomationProperties.SetName(
                window.CompletionList, $"Code completions for {document.View.Path.Value}");
            document.CompletionWindow = window;
            window.Show();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task CommitCompletionAsync(
        SourceDocumentSession document,
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeCompletionListId listId,
        WorkbenchCodeCompletionItem item,
        char? commitCharacter)
    {
        try
        {
            WorkbenchCodeCompletionCommitView result = await service.CommitCompletionAsync(
                new(snapshot, listId, item.Id, commitCharacter), cancellationToken);
            if (!document.IsCurrentInteraction(snapshot.BufferVersion) ||
                result.State is WorkbenchCodeResultState.Stale or
                    WorkbenchCodeResultState.Cancelled or WorkbenchCodeResultState.Failed)
            {
                document.SetStatus("Completion expired because the document changed.");
                return;
            }
            foreach (WorkbenchCodeTextChange change in result.Changes
                         .OrderByDescending(value => document.Editor.GetOffset(value.Range.Start)))
            {
                int start = document.Editor.GetOffset(change.Range.Start);
                int end = document.Editor.GetOffset(change.Range.End);
                document.Editor.Replace(start, Math.Max(0, end - start), change.Text.Value);
            }
            if (result.NewPosition is { } position)
                document.Editor.CaretOffset = document.Editor.GetOffset(position);
            else if (result.Changes.LastOrDefault() is { } last)
                document.Editor.CaretOffset =
                    document.Editor.GetOffset(last.Range.Start) + last.Text.Value.Length;
            if (commitCharacter is { } value && value is not '\t' and not '\n' &&
                (document.Editor.CaretOffset >= document.Editor.TextLength ||
                 document.Editor.GetCharAt(document.Editor.CaretOffset) != value))
            {
                document.Editor.Insert(document.Editor.CaretOffset, value.ToString());
                document.Editor.CaretOffset++;
            }
            document.SetStatus($"Completed {item.DisplayText.Value} with Roslyn.");
            document.Editor.Focus();
            if (commitCharacter == '(') await ShowSignatureHelpAsync(document);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    internal async Task ShowQuickInfoOnHoverAsync(
        SourceDocumentSession document,
        WorkbenchCodePosition position,
        CancellationToken hoverToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), hoverToken);
            if (document.CompletionWindow?.IsVisible is true) return;
            await ShowQuickInfoAsync(document, position);
        }
        catch (OperationCanceledException) when (hoverToken.IsCancellationRequested)
        {
        }
    }

    internal async ValueTask ShowQuickInfoAsync(
        SourceDocumentSession document,
        WorkbenchCodePosition? requestedPosition = null)
    {
        if (!DocumentIntelligence.CanUse(document)) return;
        document.CompletionWindow?.Hide();
        document.CompletionWindow = null;
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            document.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? session = await intelligence.EnsureSessionAsync(document, token);
            if (session is null) return;
            WorkbenchCodeQuickInfoView result = await service.GetQuickInfoAsync(
                DocumentIntelligence.Snapshot(document, session, version, requestedPosition), token);
            if (!document.IsCurrentInteraction(version) || result.Sections.Count == 0)
            {
                document.SetStatus("No symbol information is available at the caret.");
                return;
            }
            document.QuickInfoWindow?.Hide();
            StackPanel content = new() { Spacing = 6, MaxWidth = 760 };
            foreach (WorkbenchCodeMessage section in result.Sections)
                content.Children.Add(new TextBlock
                {
                    Text = section.Value,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new("Cascadia Code,JetBrains Mono,Consolas,Menlo,monospace"),
                });
            Border card = new() { Child = content, Padding = new(10) };
            card.Classes.Add("semantic-insight");
            AutomationProperties.SetName(card,
                $"Quick info for {document.View.Path.Value}: " +
                string.Join(" ", result.Sections.Select(section => section.Value)));
            InsightWindow window = new(document.NativeEditor.TextArea)
            {
                Child = card,
                StartOffset = result.ApplicableRange is null
                    ? document.Editor.CaretOffset
                    : document.Editor.GetOffset(result.ApplicableRange.Start),
                EndOffset = result.ApplicableRange is null
                    ? document.Editor.CaretOffset
                    : document.Editor.GetOffset(result.ApplicableRange.End),
            };
            document.QuickInfoWindow = window;
            window.Show();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    internal async ValueTask ShowSignatureHelpAsync(SourceDocumentSession document)
    {
        if (!DocumentIntelligence.CanUse(document)) return;
        document.CompletionWindow?.Hide();
        document.CompletionWindow = null;
        (WorkbenchCodeBufferVersion version, CancellationToken token) =
            document.BeginInteraction(cancellationToken);
        try
        {
            WorkbenchCodeSessionId? session = await intelligence.EnsureSessionAsync(document, token);
            if (session is null) return;
            WorkbenchCodeSignatureHelpView result = await service.GetSignatureHelpAsync(
                DocumentIntelligence.Snapshot(document, session, version), token);
            if (!document.IsCurrentInteraction(version) || result.Signatures.Count == 0) return;
            document.SignatureWindow?.Hide();
            OverloadInsightWindow window = new(document.NativeEditor.TextArea)
            {
                Provider = new RoslynOverloadProvider(result),
                StartOffset = Math.Max(0, document.Editor.CaretOffset - 1),
                EndOffset = document.Editor.TextLength,
            };
            document.SignatureWindow = window;
            window.Show();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }
}
