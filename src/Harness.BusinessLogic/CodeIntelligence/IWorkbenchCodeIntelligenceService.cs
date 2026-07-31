namespace Harness.BusinessLogic.CodeIntelligence;

public interface IWorkbenchCodeIntelligenceService
{
    ValueTask<WorkbenchCodeSessionView> StartAsync(
        WorkbenchCodeSessionRequest request,
        IProgress<WorkbenchCodeLoadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchCodeDiagnosticView> SynchronizeAsync(
        WorkbenchCodeDocumentSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<WorkbenchCodeValidationView> ValidateAsync(
        WorkbenchCodeValidationRequest request,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(
        WorkbenchCodeSessionId sessionId,
        CancellationToken cancellationToken = default);
}
