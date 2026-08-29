using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.CodeIntelligence;

namespace Harness.BusinessLogic.Tests.CodeIntelligence;

public sealed partial class WorkbenchCodeIntelligenceServiceTests
{
    private static WorkbenchCodeDocumentSnapshot Snapshot(
        WorkbenchCodeSessionId sessionId,
        long version,
        string text) => new(
        sessionId,
        new("src/App.cs"),
        new(Baseline),
        new(version),
        new(text));

    private static CodeIntelligenceDiagnosticResult ReadyDiagnostics(
        CodeIntelligenceDocumentSnapshot snapshot) => new(
        snapshot.ContextId,
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        CodeIntelligenceResultState.Ready,
        [],
        []);

    private static WorkbenchWorkspaceResolution ApprovedResolution() => new(
        new(
            new("workspace-id"),
            new GoalId("goal-id"),
            new("harness/goal-test"),
            WorkbenchWorkspaceScope.ApprovedGoalWorktree,
            "Approved goal worktree"),
        "/state/worktrees/goal-id",
        ErrorCode: null,
        Error: null);

    private static WorkbenchWorkspaceResolution OriginalResolution() => new(
        new(
            new("workspace-id"),
            null,
            new("main"),
            WorkbenchWorkspaceScope.OriginalWorkspace,
            "Original workspace"),
        "/workspace/repository",
        ErrorCode: null,
        Error: null);

    private sealed class ContextResolver(WorkbenchWorkspaceResolution result)
        : IWorkbenchWorkspaceContextResolver
    {
        internal int CallCount { get; private set; }

        public ValueTask<WorkbenchWorkspaceResolution> ResolveAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DeterministicCodeIntelligenceEngine : ICodeIntelligenceEngine
    {
        internal Func<CodeIntelligenceDocumentSnapshot, CancellationToken,
            ValueTask<CodeIntelligenceDiagnosticResult>>? Diagnostics
        { get; init; }
        internal Func<CodeIntelligenceCompletionRequest, CancellationToken,
            ValueTask<CodeIntelligenceCompletionResult>>? Completions
        { get; init; }
        internal Func<CodeIntelligenceRenamePreviewRequest, CancellationToken,
            ValueTask<CodeIntelligenceRenamePreviewResult>>? Renames
        { get; init; }
        internal Func<CodeIntelligenceDocumentTransformationPreviewRequest, CancellationToken,
            ValueTask<CodeIntelligenceDocumentTransformationPreviewResult>>? DocumentTransformations
        { get; init; }
        internal Func<CodeIntelligenceInteractiveSnapshot, CancellationToken,
            ValueTask<CodeIntelligenceMissingImportResult>>? MissingImports
        { get; init; }
        internal Func<CodeIntelligenceInteractiveSnapshot, CancellationToken,
            ValueTask<CodeIntelligenceCodeActionResult>>? CodeActions
        { get; init; }
        internal Func<CodeIntelligenceDocumentPresentationRequest, CancellationToken,
            ValueTask<CodeIntelligenceDocumentPresentationResult>>? Presentations
        { get; init; }
        internal Func<CodeIntelligenceInteractiveSnapshot, CancellationToken,
            ValueTask<CodeIntelligenceNavigationResult>>? Navigation
        { get; init; }
        internal Func<CodeIntelligenceVirtualDocumentRequest, CancellationToken,
            ValueTask<CodeIntelligenceVirtualDocumentResult>>? VirtualDocuments
        { get; init; }
        internal Func<CodeIntelligenceInspectionRequest, CancellationToken,
            ValueTask<CodeIntelligenceInspectionResult>>? Inspections
        { get; init; }
        internal CodeIntelligenceOpenRequest? OpenRequest { get; private set; }
        internal CodeIntelligenceSessionId? ClosedSession { get; private set; }
        internal int OpenCallCount { get; private set; }
        internal int ValidateCallCount { get; private set; }
        internal CodeIntelligenceValidationRequest? LastValidation { get; private set; }

        public ValueTask<CodeIntelligenceSessionResult> OpenAsync(
            CodeIntelligenceOpenRequest request,
            IProgress<CodeIntelligenceLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            OpenCallCount++;
            OpenRequest = request;
            return ValueTask.FromResult(new CodeIntelligenceSessionResult(
                request.ContextId,
                new("session-1"),
                CodeIntelligenceResultState.Ready,
                []));
        }

        public ValueTask<CodeIntelligenceDiagnosticResult> GetDiagnosticsAsync(
            CodeIntelligenceDocumentSnapshot snapshot,
            CancellationToken cancellationToken = default) => Diagnostics is null
            ? ValueTask.FromResult(ReadyDiagnostics(snapshot))
            : Diagnostics(snapshot, cancellationToken);

        public ValueTask<CodeIntelligenceValidationResult> ValidateAsync(
            CodeIntelligenceValidationRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateCallCount++;
            LastValidation = request;
            return ValueTask.FromResult(new CodeIntelligenceValidationResult(
                request.ContextId,
                request.SessionId,
                CodeIntelligenceResultState.Ready,
                CodeIntelligenceValidationDisposition.Validated,
                [],
                []));
        }

        public ValueTask<CodeIntelligenceCompletionResult> GetCompletionsAsync(
            CodeIntelligenceCompletionRequest request,
            CancellationToken cancellationToken = default) => Completions is null
            ? throw new NotSupportedException()
            : Completions(request, cancellationToken);
        public ValueTask<CodeIntelligenceCompletionCommitResult> CommitCompletionAsync(
            CodeIntelligenceCompletionCommitRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CodeIntelligenceQuickInfoResult> GetQuickInfoAsync(
            CodeIntelligenceInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CodeIntelligenceSignatureHelpResult> GetSignatureHelpAsync(
            CodeIntelligenceInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CodeIntelligenceNavigationResult> FindDefinitionAsync(
            CodeIntelligenceInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => Navigation is null
            ? throw new NotSupportedException() : Navigation(snapshot, cancellationToken);
        public ValueTask<CodeIntelligenceNavigationResult> FindReferencesAsync(
            CodeIntelligenceInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<CodeIntelligenceVirtualDocumentResult> GetVirtualDocumentAsync(
            CodeIntelligenceVirtualDocumentRequest request,
            CancellationToken cancellationToken = default) => VirtualDocuments is null
            ? throw new NotSupportedException() : VirtualDocuments(request, cancellationToken);
        public ValueTask<CodeIntelligenceInspectionResult> InspectAsync(
            CodeIntelligenceInspectionRequest request,
            CancellationToken cancellationToken = default) => Inspections is null
            ? throw new NotSupportedException() : Inspections(request, cancellationToken);
        public ValueTask<CodeIntelligenceDocumentPresentationResult> GetDocumentPresentationAsync(
            CodeIntelligenceDocumentPresentationRequest request,
            CancellationToken cancellationToken = default) => Presentations is null
            ? throw new NotSupportedException()
            : Presentations(request, cancellationToken);
        public ValueTask<CodeIntelligenceRenamePreviewResult> PreviewRenameAsync(
            CodeIntelligenceRenamePreviewRequest request,
            CancellationToken cancellationToken = default) => Renames is null
            ? throw new NotSupportedException()
            : Renames(request, cancellationToken);
        public ValueTask<CodeIntelligenceDocumentTransformationPreviewResult>
            PreviewDocumentTransformationAsync(
                CodeIntelligenceDocumentTransformationPreviewRequest request,
                CancellationToken cancellationToken = default) =>
            DocumentTransformations is null
                ? throw new NotSupportedException()
                : DocumentTransformations(request, cancellationToken);
        public ValueTask<CodeIntelligenceMissingImportResult> GetMissingImportsAsync(
            CodeIntelligenceInteractiveSnapshot snapshot,
            CancellationToken cancellationToken = default) => MissingImports is null
            ? throw new NotSupportedException()
            : MissingImports(snapshot, cancellationToken);
        public ValueTask<CodeIntelligenceCodeActionResult> GetCodeActionsAsync(
            CodeIntelligenceCodeActionRequest request,
            CancellationToken cancellationToken = default) => CodeActions is null
            ? throw new NotSupportedException()
            : CodeActions(request.Snapshot, cancellationToken);

        public ValueTask CloseAsync(
            CodeIntelligenceSessionId sessionId,
            CancellationToken cancellationToken = default)
        {
            ClosedSession = sessionId;
            return ValueTask.CompletedTask;
        }
    }
}
