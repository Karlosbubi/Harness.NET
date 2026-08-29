namespace Harness.DataAccess.CodeIntelligence;

public sealed record CodeIntelligenceTestId(string Value);
public sealed record CodeIntelligenceTestProjectPath(string Value);
public sealed record CodeIntelligenceTestName(string Value);
public sealed record CodeIntelligenceTestTraitName(string Value);
public sealed record CodeIntelligenceTestTraitValue(string Value);

public enum CodeIntelligenceTestFramework
{
    XUnit,
    NUnit,
    MSTest,
}

public sealed record CodeIntelligenceTestTrait(
    CodeIntelligenceTestTraitName Name,
    CodeIntelligenceTestTraitValue Value);

public sealed record CodeIntelligenceTestCase(
    CodeIntelligenceTestId Id,
    CodeIntelligenceTestProjectPath ProjectPath,
    CodeIntelligenceTestFramework Framework,
    CodeIntelligenceTestName FullyQualifiedName,
    CodeIntelligenceTestName DisplayName,
    CodeIntelligenceDocumentPath Path,
    CodeIntelligenceRange Range,
    IReadOnlyList<CodeIntelligenceTestTrait> Traits,
    bool IsParameterized);

public sealed record CodeIntelligenceTestDiscoveryRequest(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    string? Query,
    int MaximumResults,
    int Offset,
    CodeIntelligenceTestFramework? Framework = null);

public sealed record CodeIntelligenceTestDiscoveryResult(
    CodeIntelligenceContextId ContextId,
    CodeIntelligenceSessionId SessionId,
    CodeIntelligenceResultState State,
    IReadOnlyList<CodeIntelligenceTestCase> Tests,
    int? Continuation,
    bool IsTruncated,
    IReadOnlyList<CodeIntelligenceIssue> Issues);
