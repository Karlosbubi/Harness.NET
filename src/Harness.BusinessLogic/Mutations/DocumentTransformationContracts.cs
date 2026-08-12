using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Mutations;

public enum DocumentTransformationOrigin
{
    Human,
    Model,
}

public sealed record DocumentTransformationFileArea(string Value);

public sealed record DocumentTransformationPreviewRequest(
    string GoalId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBaselineHash BaselineHash,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeText Text,
    WorkbenchCodePosition Position,
    WorkbenchCodeDocumentTransformationKind Kind,
    WorkbenchCodeRange? Range,
    DocumentTransformationOrigin Origin,
    IReadOnlyList<DocumentTransformationFileArea> AllowedFileAreas,
    WorkbenchCodeImportNamespace? ImportNamespace = null,
    WorkbenchCodeFormattingTrigger? FormattingTrigger = null,
    WorkbenchCodeActionId? CodeActionId = null,
    WorkbenchCodeActionScope? CodeActionScope = null);

public sealed record DocumentTransformationPreviewView(
    WorkbenchCodeDocumentTransformationPreviewView? Preview,
    string? ErrorCode,
    string? Error);

public sealed record DocumentTransformationApplyRequest(
    DocumentTransformationPreviewRequest PreviewRequest,
    ToolCorrelationId CorrelationId,
    WorkbenchCodeTransformationFingerprint Fingerprint);

public sealed record DocumentTransformationApplyView(
    string GoalId,
    ToolCorrelationId CorrelationId,
    WorkbenchCodeDocumentTransformationPreviewView? Preview,
    IReadOnlyList<FileEditView> Files,
    bool WasRolledBack,
    bool WasCancelled,
    WorkbenchCodeValidationView? AppliedCodeValidation,
    string? ErrorCode,
    string? Error);
