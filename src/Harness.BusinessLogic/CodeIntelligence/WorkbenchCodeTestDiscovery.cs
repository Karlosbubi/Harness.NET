using Harness.DataAccess.CodeIntelligence;

namespace Harness.BusinessLogic.CodeIntelligence;

internal sealed partial class WorkbenchCodeIntelligenceService
{
    public async ValueTask<WorkbenchCodeTestDiscoveryView> DiscoverTestsAsync(
        WorkbenchCodeTestDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SessionId is null ||
            !sessions.TryGetValue(request.SessionId.Value, out ActiveSession? session) ||
            request.Query?.Length > 256 || request.MaximumResults is < 1 or > 2_000 ||
            request.Offset is < 0 or > 10_000)
        {
            return TestDiscoveryFailure(
                request.SessionId ?? new(string.Empty),
                "invalid_test_discovery_request",
                "An active session and bounded test query are required.");
        }

        CodeIntelligenceTestDiscoveryResult result;
        try
        {
            result = await engine.DiscoverTestsAsync(new(
                session.ContextId,
                session.SessionId,
                request.Query,
                request.MaximumResults,
                request.Offset), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return TestDiscoveryFailure(
                request.SessionId,
                "cancelled",
                "Test discovery was cancelled.",
                WorkbenchCodeResultState.Cancelled);
        }

        if (result.ContextId != session.ContextId || result.SessionId != session.SessionId)
        {
            return TestDiscoveryFailure(
                request.SessionId,
                "result_identity_mismatch",
                "Test discovery did not match the active source context.",
                WorkbenchCodeResultState.Stale);
        }

        WorkbenchCodeTestCase[] tests = result.Tests
            .Where(item => IsConfinedRelativePath(item.ProjectPath.Value) &&
                IsConfinedRelativePath(item.Path.Value) &&
                !string.IsNullOrWhiteSpace(item.Id.Value) &&
                !string.IsNullOrWhiteSpace(item.FullyQualifiedName.Value) &&
                item.Range.Start.Line >= 0 && item.Range.Start.Character >= 0 &&
                item.Range.End.Line >= item.Range.Start.Line &&
                (item.Range.End.Line > item.Range.Start.Line ||
                    item.Range.End.Character >= item.Range.Start.Character) &&
                item.Range.End.Character >= 0)
            .Take(request.MaximumResults)
            .Select(Map)
            .ToArray();
        return new(
            request.SessionId,
            Map(result.State),
            tests,
            result.Continuation is > 10_000 ? null : result.Continuation,
            result.IsTruncated,
            MapIssues(result.Issues));
    }

    private static WorkbenchCodeTestCase Map(CodeIntelligenceTestCase test) => new(
        new(test.Id.Value),
        new(test.ProjectPath.Value),
        test.Framework switch
        {
            CodeIntelligenceTestFramework.XUnit => WorkbenchCodeTestFramework.XUnit,
            CodeIntelligenceTestFramework.NUnit => WorkbenchCodeTestFramework.NUnit,
            CodeIntelligenceTestFramework.MSTest => WorkbenchCodeTestFramework.MSTest,
            _ => throw new ArgumentOutOfRangeException(nameof(test)),
        },
        new(test.FullyQualifiedName.Value),
        new(test.DisplayName.Value),
        new(test.Path.Value),
        Map(test.Range),
        test.Traits.Take(32).Select(item => new WorkbenchCodeTestTrait(
            new(item.Name.Value),
            new(item.Value.Value))).ToArray(),
        test.IsParameterized);

    private static WorkbenchCodeTestDiscoveryView TestDiscoveryFailure(
        WorkbenchCodeSessionId sessionId,
        string code,
        string message,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        sessionId,
        state,
        [],
        Continuation: null,
        IsTruncated: false,
        [Issue(code, message)]);
}
