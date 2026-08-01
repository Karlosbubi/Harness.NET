using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Mutations;

public enum RenameSymbolOrigin
{
    Human,
    Model,
}

public sealed record RenameFileArea(string Value);

public sealed record RenameSymbolPreviewRequest(
    string GoalId,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeBaselineHash BaselineHash,
    WorkbenchCodeBufferVersion BufferVersion,
    WorkbenchCodeText Text,
    WorkbenchCodePosition Position,
    WorkbenchCodeRenameName NewName,
    RenameSymbolOrigin Origin,
    IReadOnlyList<RenameFileArea> AllowedFileAreas);

public sealed record RenameSymbolPreviewView(
    WorkbenchCodeRenamePreviewView? Preview,
    string? ErrorCode,
    string? Error);

public sealed record RenameSymbolApplyRequest(
    RenameSymbolPreviewRequest PreviewRequest,
    ToolCorrelationId CorrelationId,
    WorkbenchCodeTransformationFingerprint Fingerprint);

public sealed record RenameSymbolApplyView(
    string GoalId,
    ToolCorrelationId CorrelationId,
    WorkbenchCodeRenamePreviewView? Preview,
    IReadOnlyList<FileEditView> Files,
    bool WasRolledBack,
    bool WasCancelled,
    WorkbenchCodeValidationView? AppliedCodeValidation,
    string? ErrorCode,
    string? Error);
