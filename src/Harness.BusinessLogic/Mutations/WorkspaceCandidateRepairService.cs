using Harness.BusinessLogic.CodeIntelligence;

namespace Harness.BusinessLogic.Mutations;

internal sealed partial class WorkspaceMutationService
{
    private const int MaximumDeterministicCandidateRepairs = 4;

    private async ValueTask<CandidateRepairOutcome> TryRepairCandidateAsync(
        WorkbenchCodeSessionId sessionId,
        WorkbenchCodeDocumentPath path,
        WorkbenchCodeBaselineHash baseline,
        WorkbenchCodeText originalContent,
        WorkbenchCodeValidationView originalValidation,
        CancellationToken cancellationToken)
    {
        if (IsWarningFreeModelEdit(originalValidation) ||
            originalValidation.Disposition is not WorkbenchCodeValidationDisposition.Rejected ||
            originalValidation.State is not (WorkbenchCodeResultState.Ready or
                WorkbenchCodeResultState.Degraded) ||
            originalValidation.Issues.Count > 0)
        {
            return new(originalContent, originalValidation, []);
        }

        WorkbenchCodeText content = originalContent;
        WorkbenchCodeValidationView validation = originalValidation;
        List<FileEditDeterministicRepairView> repairs = [];
        for (int attempt = 0; attempt < MaximumDeterministicCandidateRepairs; attempt++)
        {
            if (validation.Issues.Count > 0)
            {
                return new(originalContent, originalValidation, []);
            }

            WorkbenchCodeValidationDiagnostic[] blockers = BlockingDiagnostics(validation);
            if (blockers.Length == 0)
            {
                return repairs.Count > 0 && IsWarningFreeModelEdit(validation)
                    ? new(content, validation, repairs)
                    : new(originalContent, originalValidation, []);
            }

            if (blockers.Length > MaximumDeterministicCandidateRepairs - attempt ||
                blockers.Any(item => !IsRepairableMissingImport(item, path)))
            {
                return new(originalContent, originalValidation, []);
            }

            WorkbenchCodeDiagnostic diagnostic = blockers
                .OrderBy(item => item.Diagnostic.Range.Start.Line)
                .ThenBy(item => item.Diagnostic.Range.Start.Character)
                .First()
                .Diagnostic;
            WorkbenchCodeBufferVersion bufferVersion = new(attempt + 1);
            WorkbenchCodeInteractiveSnapshot snapshot = new(
                sessionId,
                path,
                baseline,
                bufferVersion,
                content,
                diagnostic.Range.Start);
            WorkbenchCodeMissingImportView missing =
                await codeIntelligenceService!.GetMissingImportsAsync(snapshot, cancellationToken);
            if (missing.State is not WorkbenchCodeResultState.Ready ||
                missing.Candidates.Count != 1)
            {
                return new(originalContent, originalValidation, []);
            }

            WorkbenchCodeMissingImportCandidate candidate = missing.Candidates[0];
            WorkbenchCodeDocumentTransformationPreviewView preview =
                await codeIntelligenceService.PreviewDocumentTransformationAsync(
                    new(
                        snapshot,
                        WorkbenchCodeDocumentTransformationKind.AddMissingImport,
                        Range: null,
                        candidate.Namespace),
                    cancellationToken);
            WorkbenchCodeDocumentTransformationEdit? edit = preview.Edits.Count == 1
                ? preview.Edits[0]
                : null;
            if (preview.State is not WorkbenchCodeResultState.Ready ||
                preview.Disposition is not WorkbenchCodeTransformationDisposition.Ready ||
                edit is null || edit.Path != path || edit.BaselineHash != baseline ||
                edit.OriginalText != content || edit.Text == content)
            {
                return new(originalContent, originalValidation, []);
            }

            content = edit.Text;
            repairs.Add(new(
                FileEditDeterministicRepairKind.AddMissingImport,
                diagnostic.Id,
                candidate.Namespace,
                candidate.Symbol));
            validation = await codeIntelligenceService.ValidateAsync(
                new(
                    sessionId,
                    WorkbenchCodeValidationPhase.Candidate,
                    [new(path, baseline, content)]),
                cancellationToken);
        }

        return IsWarningFreeModelEdit(validation)
            ? new(content, validation, repairs)
            : new(originalContent, originalValidation, []);
    }

    private static WorkbenchCodeValidationDiagnostic[] BlockingDiagnostics(
        WorkbenchCodeValidationView validation) => validation.Diagnostics
        .Where(item => item.Kind is WorkbenchCodeDiagnosticDeltaKind.Introduced &&
            item.Diagnostic.Severity is WorkbenchCodeDiagnosticSeverity.Warning or
                WorkbenchCodeDiagnosticSeverity.Error)
        .ToArray();

    private static bool IsRepairableMissingImport(
        WorkbenchCodeValidationDiagnostic diagnostic,
        WorkbenchCodeDocumentPath path) =>
        diagnostic.Diagnostic.Severity is WorkbenchCodeDiagnosticSeverity.Error &&
        diagnostic.Diagnostic.Source.Value.Equals("Compiler", StringComparison.Ordinal) &&
        diagnostic.Diagnostic.Path == path &&
        diagnostic.Diagnostic.Id.Value is "CS0246" or "CS0103";

    private sealed record CandidateRepairOutcome(
        WorkbenchCodeText Content,
        WorkbenchCodeValidationView Validation,
        IReadOnlyList<FileEditDeterministicRepairView> Repairs);
}
