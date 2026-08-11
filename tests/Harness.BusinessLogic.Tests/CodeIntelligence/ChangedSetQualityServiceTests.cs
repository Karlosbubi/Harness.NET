using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Tests.CodeIntelligence;

public sealed class ChangedSetQualityServiceTests
{
    [Fact]
    public async Task Passes_only_clean_changed_roslyn_files_with_build_and_test_evidence()
    {
        ChangedSetQualityService service = new(new Inspection("class C {}"),
            new Code([]), new Evidence(success: true), TimeProvider.System);

        ChangedSetQualityView result = await service.EvaluateAsync(new("goal"));

        Assert.Equal(ChangedSetQualityDisposition.Passed, result.Disposition);
        Assert.Equal(1, result.CSharpFilesChecked);
        Assert.True(result.BuildPassed);
        Assert.True(result.TestsPassed);
    }

    [Fact]
    public async Task Fails_deterministically_on_compiler_error_or_placeholder()
    {
        WorkbenchCodeDiagnostic error = new(new("CS1002"), new("; expected"), new("Roslyn"),
            new("Fixture"), new("src/C.cs"), new(new(0, 0), new(0, 1)),
            WorkbenchCodeDiagnosticSeverity.Error);
        ChangedSetQualityService service = new(new Inspection("TODO NotImplementedException"),
            new Code([error]), new Evidence(success: true), TimeProvider.System);

        ChangedSetQualityView result = await service.EvaluateAsync(new("goal"));

        Assert.Equal(ChangedSetQualityDisposition.Failed, result.Disposition);
        Assert.Contains(result.Findings, item => item.Contains("placeholder", StringComparison.Ordinal));
    }

    private sealed class Inspection(string diff) : IGoalWorkspaceInspectionService
    {
        public ValueTask<WorkspaceGitStateView> InspectGitAsync(GoalId goalId,
            GoalWorkspaceScope scope, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkspaceGitStateView("goal", "head",
                [new("src/C.cs", "Modified")], diff, false, null, null));
        public ValueTask<GoalTreeView> ListTreeAsync(GoalId goalId, GoalWorkspaceScope scope,
            string relativeRoot, string? glob, int maximumDepth, int maximumResults,
            string? continuation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GoalTreeView(new(new("source"), "workspace", "goal",
                scope, "goal", "Fixture.slnx"), [], null, false, null, null));
        public ValueTask<WorkspaceFileView> ReadFileAsync(GoalId goalId, GoalWorkspaceScope scope,
            string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkspaceTextSearchView> SearchTextAsync(GoalId goalId, GoalWorkspaceScope scope,
            string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkspaceDotNetInfoView> InspectDotNetAsync(GoalId goalId, GoalWorkspaceScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Code(IReadOnlyList<WorkbenchCodeDiagnostic> diagnostics)
        : IGoalCodeIntelligenceService
    {
        public ValueTask<GoalCodeProblemsView> InspectProblemsAsync(GoalId goalId,
            GoalWorkspaceScope scope, WorkbenchCodeDocumentPath path,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new GoalCodeProblemsView(path, WorkbenchCodeResultState.Ready, diagnostics, null));
        public ValueTask<GoalCodeSymbolView> GetSymbolAsync(GoalId goalId, GoalWorkspaceScope scope,
            WorkbenchCodeDocumentPath path, WorkbenchCodePosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GoalCodeNavigationView> FindDefinitionAsync(GoalId goalId, GoalWorkspaceScope scope,
            WorkbenchCodeDocumentPath path, WorkbenchCodePosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GoalCodeNavigationView> FindReferencesAsync(GoalId goalId, GoalWorkspaceScope scope,
            WorkbenchCodeDocumentPath path, WorkbenchCodePosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GoalCodeNavigationView> FindImplementationsAsync(GoalId goalId, GoalWorkspaceScope scope,
            WorkbenchCodeDocumentPath path, WorkbenchCodePosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Evidence(bool success) : IToolEvidenceService
    {
        public ValueTask<ToolEvidenceSnapshot> ListAsync(
            string goalId, CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ToolEvidenceView Item(ToolKind kind) => new(new(Guid.NewGuid().ToString("N")), goalId,
                new(kind.ToString()), kind, "{}",
                success ? ToolEvidenceState.Succeeded : ToolEvidenceState.Failed,
                "{}", now, now);
            return ValueTask.FromResult(new ToolEvidenceSnapshot(
                [Item(ToolKind.Build), Item(ToolKind.Test)], null, null));
        }
    }
}
