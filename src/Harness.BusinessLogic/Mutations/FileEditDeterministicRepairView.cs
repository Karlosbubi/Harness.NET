using Harness.BusinessLogic.CodeIntelligence;

namespace Harness.BusinessLogic.Mutations;

public enum FileEditDeterministicRepairKind
{
    AddMissingImport,
}

public sealed record FileEditDeterministicRepairView(
    FileEditDeterministicRepairKind Kind,
    WorkbenchCodeDiagnosticId DiagnosticId,
    WorkbenchCodeImportNamespace Namespace,
    WorkbenchCodeImportSymbol Symbol);
