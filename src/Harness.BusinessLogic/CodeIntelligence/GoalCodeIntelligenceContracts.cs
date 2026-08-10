namespace Harness.BusinessLogic.CodeIntelligence;

public sealed record GoalCodeProblemsView(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeDiagnostic> Diagnostics,
    WorkbenchCodeIssue? Issue);

public sealed record GoalCodeSymbolView(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodePosition Position,
    WorkbenchCodeResultState State,
    WorkbenchCodeRange? ApplicableRange,
    IReadOnlyList<WorkbenchCodeMessage> Sections,
    WorkbenchCodeIssue? Issue);

public sealed record GoalCodeNavigationView(
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodePosition Position,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeSymbolDestination> Destinations,
    WorkbenchCodeIssue? Issue);
