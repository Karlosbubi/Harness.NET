using System.Reflection;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Execution;

namespace Harness.BusinessLogic.Tests.CodeIntelligence;

public sealed class RoslynDeveloperTestIdentityVerifierTests
{
    [Fact]
    public async Task Verifies_exact_semantic_identity_and_always_closes_the_Roslyn_session()
    {
        IWorkbenchCodeIntelligenceService code =
            DispatchProxy.Create<IWorkbenchCodeIntelligenceService, CodeProxy>();
        CodeProxy proxy = (CodeProxy)(object)code;
        RoslynDeveloperTestIdentityVerifier verifier = new(code);
        DeveloperTestTarget test = new(
            new(new string('a', 64)), new("Demo.Tests.Exact"));

        DeveloperTestIdentityVerification verified = await verifier.VerifyExactAsync(
            new(new("workspace-1"), null),
            new(new("tests/App.Tests.csproj"), new("net10.0"), null),
            test);

        Assert.True(verified.IsVerified);
        Assert.Equal("tests/ExactTests.cs", verified.Source?.Value);
        Assert.Equal(18, verified.Line?.Value);
        Assert.Equal(1, proxy.Stops);
    }

    [Fact]
    public async Task Rejects_stale_identity_and_still_closes_the_Roslyn_session()
    {
        IWorkbenchCodeIntelligenceService code =
            DispatchProxy.Create<IWorkbenchCodeIntelligenceService, CodeProxy>();
        CodeProxy proxy = (CodeProxy)(object)code;
        RoslynDeveloperTestIdentityVerifier verifier = new(code);
        DeveloperTestTarget stale = new(
            new(new string('b', 64)), new("Demo.Tests.Exact"));

        DeveloperTestIdentityVerification result = await verifier.VerifyExactAsync(
            new(new("workspace-1"), null),
            new(new("tests/App.Tests.csproj"), null, null),
            stale);

        Assert.False(result.IsVerified);
        Assert.Equal("test_debug_target_stale", result.ErrorCode);
        Assert.Equal(1, proxy.Stops);
    }

    public class CodeProxy : DispatchProxy
    {
        public int Stops { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IWorkbenchCodeIntelligenceService.StartAsync) =>
                    ValueTask.FromResult<WorkbenchCodeSessionView>(new(
                        new("context"), new("session"),
                        WorkbenchCodeResultState.Ready, [])),
                nameof(IWorkbenchCodeIntelligenceService.DiscoverTestsAsync) =>
                    ValueTask.FromResult<WorkbenchCodeTestDiscoveryView>(new(
                        new("session"), WorkbenchCodeResultState.Ready,
                        [new(
                            new(new string('a', 64)),
                            new("tests/App.Tests.csproj"),
                            WorkbenchCodeTestFramework.XUnit,
                            new("Demo.Tests.Exact"),
                            new("Exact"),
                            new("tests/ExactTests.cs"),
                            new(new(17, 4), new(19, 5)),
                            [],
                            false)],
                        null, false, [])),
                nameof(IWorkbenchCodeIntelligenceService.StopAsync) => Stop(),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };

        private ValueTask Stop()
        {
            Stops++;
            return ValueTask.CompletedTask;
        }
    }
}
