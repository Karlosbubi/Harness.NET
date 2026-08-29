namespace Harness.BusinessLogic.CodeIntelligence;

public sealed record WorkbenchCodeTestId(string Value);
public sealed record WorkbenchCodeTestProjectPath(string Value);
public sealed record WorkbenchCodeTestName(string Value);
public sealed record WorkbenchCodeTestTraitName(string Value);
public sealed record WorkbenchCodeTestTraitValue(string Value);

public enum WorkbenchCodeTestFramework
{
    XUnit,
    NUnit,
    MSTest,
}

public sealed record WorkbenchCodeTestTrait(
    WorkbenchCodeTestTraitName Name,
    WorkbenchCodeTestTraitValue Value);

public sealed record WorkbenchCodeTestCase(
    WorkbenchCodeTestId Id,
    WorkbenchCodeTestProjectPath ProjectPath,
    WorkbenchCodeTestFramework Framework,
    WorkbenchCodeTestName FullyQualifiedName,
    WorkbenchCodeTestName DisplayName,
    WorkbenchCodeDocumentPath Path,
    WorkbenchCodeRange Range,
    IReadOnlyList<WorkbenchCodeTestTrait> Traits,
    bool IsParameterized);

public sealed record WorkbenchCodeTestDiscoveryRequest(
    WorkbenchCodeSessionId SessionId,
    string? Query,
    int MaximumResults,
    int Offset);

public sealed record WorkbenchCodeTestDiscoveryView(
    WorkbenchCodeSessionId SessionId,
    WorkbenchCodeResultState State,
    IReadOnlyList<WorkbenchCodeTestCase> Tests,
    int? Continuation,
    bool IsTruncated,
    IReadOnlyList<WorkbenchCodeIssue> Issues);
