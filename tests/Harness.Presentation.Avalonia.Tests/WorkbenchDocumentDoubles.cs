using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    private sealed class DocumentService : IWorkbenchDocumentService
    {
        internal bool Editable { get; init; } = true;
        internal string Content { get; set; } = "namespace Example;";
        internal List<WorkbenchDocumentSaveRequest> SaveRequests { get; } = [];
        internal Queue<WorkbenchDocumentSaveResult> SaveResults { get; } = [];

        public ValueTask<WorkbenchDocumentView> OpenAsync(
            WorkbenchDocumentOpenRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkbenchDocumentView(
                request.WorkspaceId,
                Editable ? request.GoalId : null,
                Editable && request.GoalId is not null ? new("harness/goal-1") : null,
                request.Path,
                new(Content),
                new("7755c09dd3d9f796fe7f9d6225f6f71309e31eba460d4c0517cbde6ba34488f4"),
                new(Content.Length),
                IsTruncated: false,
                Editable ? WorkbenchDocumentAccess.Editable : WorkbenchDocumentAccess.ReadOnly,
                Editable
                    ? request.GoalId is null
                        ? "Editing the active trusted workspace."
                        : "Editing isolated branch harness/goal-1."
                    : "Read-only source.",
                ErrorCode: null,
                Error: null));

        public ValueTask<WorkbenchDocumentSaveResult> SaveAsync(
            WorkbenchDocumentSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveRequests.Add(request);
            return ValueTask.FromResult(SaveResults.TryDequeue(out WorkbenchDocumentSaveResult? result)
                ? result
                : new WorkbenchDocumentSaveResult(
                    request.WorkspaceId,
                    request.GoalId,
                    request.CorrelationId,
                    request.Path,
                    request.ExpectedSha256,
                    request.ExpectedSha256,
                    new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                    new(request.Content.Value.Length),
                    WorkbenchDocumentSaveOutcome.Saved,
                    ErrorCode: null,
                    Error: null));
        }
    }

    private sealed class DocumentPrompt : IWorkbenchDocumentPrompt
    {
        internal Queue<WorkbenchUnsavedDecision> UnsavedDecisions { get; } = [];
        internal Queue<WorkbenchConflictDecision> ConflictDecisions { get; } = [];
        internal List<WorkbenchUnsavedPrompt> UnsavedPrompts { get; } = [];
        internal List<WorkbenchConflictPrompt> ConflictPrompts { get; } = [];
        internal Queue<bool> GitDestructiveDecisions { get; } = [];
        internal List<DeveloperGitDestructivePreviewView> GitDestructivePreviews { get; } = [];
        internal Queue<DeveloperGitCommitDraft?> GitCommitDrafts { get; } = [];
        internal Queue<bool> GitCommitDecisions { get; } = [];
        internal List<DeveloperGitCommitPreviewView> GitCommitPreviews { get; } = [];
        internal Queue<bool> GitBranchDeleteDecisions { get; } = [];
        internal List<DeveloperGitBranchDeletePreviewView> GitBranchDeletePreviews { get; } = [];
        internal Queue<bool> GitTagDeleteDecisions { get; } = [];
        internal List<DeveloperGitTagDeletePreviewView> GitTagDeletePreviews { get; } = [];
        internal Queue<bool> GitWorktreeRemoveDecisions { get; } = [];
        internal List<DeveloperGitWorktreeRemovePreviewView> GitWorktreeRemovePreviews { get; } = [];
        internal Queue<bool> GitStashDropDecisions { get; } = [];
        internal List<DeveloperGitStashDropPreviewView> GitStashDropPreviews { get; } = [];

        public ValueTask<WorkbenchUnsavedDecision> DecideUnsavedAsync(
            WorkbenchUnsavedPrompt prompt,
            Window? owner)
        {
            UnsavedPrompts.Add(prompt);
            return ValueTask.FromResult(UnsavedDecisions.TryDequeue(out WorkbenchUnsavedDecision decision)
                ? decision
                : WorkbenchUnsavedDecision.Cancel);
        }

        public ValueTask<WorkbenchConflictDecision> DecideConflictAsync(
            WorkbenchConflictPrompt prompt,
            Window? owner)
        {
            ConflictPrompts.Add(prompt);
            return ValueTask.FromResult(ConflictDecisions.TryDequeue(out WorkbenchConflictDecision decision)
                ? decision
                : WorkbenchConflictDecision.Cancel);
        }

        public ValueTask<bool> ConfirmGitDestructiveAsync(
            DeveloperGitDestructivePreviewView preview,
            Window? owner)
        {
            GitDestructivePreviews.Add(preview);
            return ValueTask.FromResult(GitDestructiveDecisions.TryDequeue(out bool decision) && decision);
        }

        public ValueTask<DeveloperGitCommitDraft?> CollectGitCommitAsync(Window? owner) =>
            ValueTask.FromResult(GitCommitDrafts.TryDequeue(out DeveloperGitCommitDraft? draft)
                ? draft : null);

        public ValueTask<bool> ConfirmGitCommitAsync(
            DeveloperGitCommitPreviewView preview,
            Window? owner)
        {
            GitCommitPreviews.Add(preview);
            return ValueTask.FromResult(GitCommitDecisions.TryDequeue(out bool decision) && decision);
        }

        public ValueTask<bool> ConfirmGitBranchDeleteAsync(
            DeveloperGitBranchDeletePreviewView preview,
            Window? owner)
        {
            GitBranchDeletePreviews.Add(preview);
            return ValueTask.FromResult(GitBranchDeleteDecisions.TryDequeue(out bool decision) && decision);
        }

        public ValueTask<bool> ConfirmGitTagDeleteAsync(
            DeveloperGitTagDeletePreviewView preview,
            Window? owner)
        {
            GitTagDeletePreviews.Add(preview);
            return ValueTask.FromResult(GitTagDeleteDecisions.TryDequeue(out bool decision) && decision);
        }

        public ValueTask<bool> ConfirmGitWorktreeRemoveAsync(
            DeveloperGitWorktreeRemovePreviewView preview,
            Window? owner)
        {
            GitWorktreeRemovePreviews.Add(preview);
            return ValueTask.FromResult(GitWorktreeRemoveDecisions.TryDequeue(out bool decision) && decision);
        }

        public ValueTask<bool> ConfirmGitStashDropAsync(
            DeveloperGitStashDropPreviewView preview,
            Window? owner)
        {
            GitStashDropPreviews.Add(preview);
            return ValueTask.FromResult(GitStashDropDecisions.TryDequeue(out bool decision) && decision);
        }


        public ValueTask<bool> ConfirmGitRemoteAsync(
            DeveloperGitRemotePreviewView preview,
            Window? owner) => ValueTask.FromResult(false);
    }

    private sealed class CodeIntelligenceService : IWorkbenchCodeIntelligenceService
    {
        internal Func<WorkbenchCodeDocumentSnapshot, WorkbenchCodeDiagnosticView>? Diagnostics
        {
            get;
            init;
        }

        internal List<WorkbenchCodeDocumentSnapshot> Snapshots { get; } = [];
        internal List<WorkbenchCodeSessionRequest> StartRequests { get; } = [];
        internal List<WorkbenchCodeSessionId> StoppedSessions { get; } = [];
        internal Func<WorkbenchCodeCompletionRequest, WorkbenchCodeCompletionView>? Completions
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeCompletionCommitRequest,
        WorkbenchCodeCompletionCommitView>? CompletionCommit
        { get; init; }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeQuickInfoView>? QuickInfo
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeSignatureHelpView>? Signatures
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeNavigationView>? Definition
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeNavigationView>? References
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeNavigationView>? Implementations
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeVirtualDocumentRequest, WorkbenchCodeVirtualDocumentView>?
            VirtualDocument
        { get; init; }
        internal Func<WorkbenchCodeInspectionRequest, WorkbenchCodeInspectionView>? Inspection
        { get; init; }
        internal Func<
            WorkbenchCodeDocumentPresentationRequest,
            WorkbenchCodeDocumentPresentationView>? Presentation
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeOccurrenceView>? Occurrences
        {
            get;
            init;
        }
        internal Func<WorkbenchCodeDocumentTransformationPreviewRequest,
            WorkbenchCodeDocumentTransformationPreviewView>? DocumentTransformations
        { get; init; }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeMissingImportView>? MissingImports
        { get; init; }
        internal Func<WorkbenchCodeInteractiveSnapshot, WorkbenchCodeActionView>? CodeActions
        { get; init; }
        internal int ImplementationCallCount { get; private set; }

        public ValueTask<WorkbenchCodeSessionView> StartAsync(
            WorkbenchCodeSessionRequest request,
            IProgress<WorkbenchCodeLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            StartRequests.Add(request);
            return ValueTask.FromResult<WorkbenchCodeSessionView>(new(
                new("context-1"),
                new("session-1"),
                WorkbenchCodeResultState.Ready,
                []));
        }

        public ValueTask<WorkbenchCodeDiagnosticView> SynchronizeAsync(
            WorkbenchCodeDocumentSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            Snapshots.Add(snapshot);
            return ValueTask.FromResult(Diagnostics?.Invoke(snapshot) ?? new(
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready,
                [],
                []));
        }

        public ValueTask<WorkbenchCodeValidationView> ValidateAsync(
            WorkbenchCodeValidationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkbenchCodeValidationView>(new(
            request.SessionId,
            WorkbenchCodeResultState.Degraded,
            WorkbenchCodeValidationDisposition.NotApplicable,
            [],
            []));

        public ValueTask<WorkbenchCodeCompletionView> GetCompletionsAsync(
            WorkbenchCodeCompletionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Completions?.Invoke(request) ?? new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    null,
                    new(request.Snapshot.Position, request.Snapshot.Position),
                    [],
                    []));
        public ValueTask<WorkbenchCodeCompletionCommitView> CommitCompletionAsync(
            WorkbenchCodeCompletionCommitRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                CompletionCommit?.Invoke(request) ?? new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Stale,
                    [],
                    null,
                    [new(new("completion_unavailable"), new("Completion is unavailable."))]));
        public ValueTask<WorkbenchCodeQuickInfoView> GetQuickInfoAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                QuickInfo?.Invoke(snapshot) ?? new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready, null, [], []));
        public ValueTask<WorkbenchCodeSignatureHelpView> GetSignatureHelpAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Signatures?.Invoke(snapshot) ?? new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready, [], 0, 0, []));
        public ValueTask<WorkbenchCodeNavigationView> FindDefinitionAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Definition?.Invoke(snapshot) ?? new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready, [], []));
        public ValueTask<WorkbenchCodeNavigationView> FindReferencesAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                References?.Invoke(snapshot) ?? new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready, [], []));
        public ValueTask<WorkbenchCodeNavigationView> FindImplementationsAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            ImplementationCallCount++;
            return ValueTask.FromResult(
                Implementations?.Invoke(snapshot) ?? new(
                    snapshot.SessionId, snapshot.Path, snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready, [], []));
        }

        public ValueTask<WorkbenchCodeVirtualDocumentView> GetVirtualDocumentAsync(
            WorkbenchCodeVirtualDocumentRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                VirtualDocument?.Invoke(request) ?? new(
                    request.Snapshot.SessionId, request.Snapshot.Path,
                    request.Snapshot.BufferVersion, WorkbenchCodeResultState.Failed,
                    request.Id, null, null, null, null, null, true,
                    [new(new("virtual_document_unavailable"),
                        new("Virtual source is unavailable."))]));

        public ValueTask<WorkbenchCodeInspectionView> InspectAsync(
            WorkbenchCodeInspectionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Inspection?.Invoke(request) ?? new(
                    request.Snapshot.SessionId, request.Snapshot.Path,
                    request.Snapshot.BufferVersion, WorkbenchCodeResultState.Failed,
                    request.Kind, null, null, null, true, false,
                    [new(new("inspection_unavailable"),
                        new("Code inspection is unavailable."))]));

        public ValueTask<WorkbenchCodeDocumentPresentationView> GetDocumentPresentationAsync(
            WorkbenchCodeDocumentPresentationRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Presentation?.Invoke(request) ?? new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    [], [], [], [], [], [], false, []));

        public ValueTask<WorkbenchCodeOccurrenceView> FindOccurrencesAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                Occurrences?.Invoke(snapshot) ?? new(
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    WorkbenchCodeResultState.Ready,
                    null,
                    [],
                    false,
                    []));

        public ValueTask<WorkbenchCodeDocumentTransformationPreviewView>
            PreviewDocumentTransformationAsync(
                WorkbenchCodeDocumentTransformationPreviewRequest request,
                CancellationToken cancellationToken = default) => ValueTask.FromResult(
                DocumentTransformations?.Invoke(request) ?? new(
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    WorkbenchCodeResultState.Failed,
                    WorkbenchCodeTransformationDisposition.Rejected,
                    request.Kind,
                    request.Range,
                    Edits: [],
                    [],
                    [],
                    Fingerprint: null,
                    [new(new("document_transformation_unavailable"),
                        new("Document transformation is unavailable."))]));

        public ValueTask<WorkbenchCodeMissingImportView> GetMissingImportsAsync(
            WorkbenchCodeInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            MissingImports?.Invoke(snapshot) ?? new(
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready,
                [],
                []));

        public ValueTask<WorkbenchCodeActionView> GetCodeActionsAsync(
            WorkbenchCodeActionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            CodeActions?.Invoke(request.Snapshot) ?? new(
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                WorkbenchCodeResultState.Ready,
                [],
                []));

        public ValueTask StopAsync(
            WorkbenchCodeSessionId sessionId,
            CancellationToken cancellationToken = default)
        {
            StoppedSessions.Add(sessionId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MutationService : IWorkspaceMutationService
    {
        private const string Fingerprint =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string NewHash =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        internal RenameSymbolPreviewRequest? PreviewRequest { get; private set; }
        internal int ApplyCallCount { get; private set; }
        internal DocumentTransformationApplyRequest? DocumentApplyRequest { get; private set; }
        internal int DocumentApplyCallCount { get; private set; }

        public ValueTask<RenameSymbolPreviewView> PreviewRenameAsync(
            RenameSymbolPreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            PreviewRequest = request;
            return ValueTask.FromResult(new RenameSymbolPreviewView(
                Preview(request),
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<RenameSymbolApplyView> ApplyRenameAsync(
            RenameSymbolApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            ApplyCallCount++;
            WorkbenchCodeRenamePreviewView preview = Preview(request.PreviewRequest);
            return ValueTask.FromResult(new RenameSymbolApplyView(
                request.PreviewRequest.GoalId,
                request.CorrelationId,
                preview,
                [new(
                    request.PreviewRequest.GoalId,
                    request.CorrelationId,
                    "src/App.cs",
                    request.PreviewRequest.BaselineHash.Value,
                    NewHash,
                    "namespace Renamed;".Length,
                    WasCreated: false,
                    ErrorCode: null,
                    Error: null)],
                WasRolledBack: false,
                WasCancelled: false,
                new(
                    new("session-1"),
                    WorkbenchCodeResultState.Ready,
                    WorkbenchCodeValidationDisposition.Validated,
                    [],
                    []),
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<FileEditView> ApplyFileEditAsync(
            FileEditRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<DocumentTransformationApplyView> ApplyDocumentTransformationAsync(
            DocumentTransformationApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            DocumentApplyRequest = request;
            DocumentApplyCallCount++;
            DocumentTransformationPreviewRequest source = request.PreviewRequest;
            WorkbenchCodeDocumentTransformationPreviewView preview = new(
                new("session-1"),
                source.Path,
                source.BufferVersion,
                WorkbenchCodeResultState.Ready,
                WorkbenchCodeTransformationDisposition.Ready,
                source.Kind,
                source.Range,
                [
                    new(source.Path, source.BaselineHash, source.Text,
                        new(source.Text.Value + "// transformed\n"), 1),
                    new(new("src/Other.cs"), new(Fingerprint), new("class Other { }\n"),
                        new("class Other { void Changed() { } }\n"), 1),
                ],
                [],
                [],
                new(Fingerprint),
                [],
                source.ImportNamespace,
                source.FormattingTrigger,
                source.CodeActionId,
                source.CodeActionScope);
            return ValueTask.FromResult(new DocumentTransformationApplyView(
                source.GoalId,
                request.CorrelationId,
                preview,
                preview.Edits.Select(edit => new FileEditView(
                    source.GoalId,
                    request.CorrelationId,
                    edit.Path.Value,
                    edit.BaselineHash.Value,
                    NewHash,
                    edit.Text.Value.Length,
                    WasCreated: false,
                    ErrorCode: null,
                    Error: null)).ToArray(),
                WasRolledBack: false,
                WasCancelled: false,
                new(new("session-1"), WorkbenchCodeResultState.Ready,
                    WorkbenchCodeValidationDisposition.Validated, [], []),
                ErrorCode: null,
                Error: null));
        }

        public ValueTask<DotNetOperationView> RunDotNetAsync(
            DotNetOperationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static WorkbenchCodeRenamePreviewView Preview(RenameSymbolPreviewRequest request) => new(
            new("session-1"),
            request.Path,
            request.BufferVersion,
            WorkbenchCodeResultState.Ready,
            WorkbenchCodeTransformationDisposition.Ready,
            new("Class|Example"),
            request.NewName,
            [new(
                request.Path,
                request.BaselineHash,
                request.Text,
                new("namespace Renamed;"),
                1)],
            [],
            [],
            new(Fingerprint),
            []);
    }

    private sealed class LayoutService : IWorkbenchLayoutService
    {
        internal string? Stored { get; set; }
        internal bool WasReset { get; private set; }

        public ValueTask<WorkbenchLayoutLoadResult> LoadAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            Stored is null
                ? new WorkbenchLayoutLoadResult(WorkbenchLayoutLoadState.Missing, null, null)
                : new WorkbenchLayoutLoadResult(
                    WorkbenchLayoutLoadState.Available,
                    new(Stored),
                    null));

        public ValueTask<WorkbenchLayoutWriteResult> SaveAsync(
            WorkbenchLayoutPayload layout,
            CancellationToken cancellationToken = default)
        {
            Stored = layout.Value;
            return ValueTask.FromResult(new WorkbenchLayoutWriteResult(true, null));
        }

        public ValueTask<WorkbenchLayoutWriteResult> ResetAsync(
            CancellationToken cancellationToken = default)
        {
            Stored = null;
            WasReset = true;
            return ValueTask.FromResult(new WorkbenchLayoutWriteResult(true, null));
        }
    }}
