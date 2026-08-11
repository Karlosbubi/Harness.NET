using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;

namespace Harness.BusinessLogic.CodeIntelligence;

public enum ChangedSetQualityDisposition { Passed, Failed, Incomplete }
public sealed record ChangedSetQualityView(
    GoalInspectionIdentity? Identity,
    ChangedSetQualityDisposition Disposition,
    int ChangedFiles,
    int CSharpFilesChecked,
    IReadOnlyList<WorkbenchCodeDiagnostic> Diagnostics,
    bool BuildPassed,
    bool TestsPassed,
    IReadOnlyList<string> Findings,
    bool IsTruncated,
    DateTimeOffset EvaluatedAt);

internal interface IChangedSetQualityService
{
    ValueTask<ChangedSetQualityView> EvaluateAsync(
        GoalId goalId, CancellationToken cancellationToken = default);
}

internal sealed class ChangedSetQualityService(
    IGoalWorkspaceInspectionService inspectionService,
    IGoalCodeIntelligenceService codeIntelligenceService,
    IToolEvidenceService evidenceService,
    TimeProvider timeProvider) : IChangedSetQualityService
{
    private const int MaximumFiles = 100;

    public async ValueTask<ChangedSetQualityView> EvaluateAsync(
        GoalId goalId, CancellationToken cancellationToken = default)
    {
        WorkspaceGitStateView git = await inspectionService.InspectGitAsync(
            goalId, GoalWorkspaceScope.ApprovedWorktree, cancellationToken);
        GoalTreeView identity = await inspectionService.ListTreeAsync(goalId,
            GoalWorkspaceScope.ApprovedWorktree, string.Empty, null, 0, 1, null, cancellationToken);
        if (git.ErrorCode is not null)
            return new(identity.Identity, ChangedSetQualityDisposition.Incomplete, 0, 0, [],
                false, false, [git.Error ?? "The approved worktree is unavailable."], false,
                timeProvider.GetUtcNow());

        string[] changedCSharp = git.Changes.Select(change => change.Path)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal).Take(MaximumFiles + 1).ToArray();
        bool truncated = changedCSharp.Length > MaximumFiles;
        List<WorkbenchCodeDiagnostic> diagnostics = [];
        List<string> findings = [];
        foreach (string path in changedCSharp.Take(MaximumFiles))
        {
            GoalCodeProblemsView result = await codeIntelligenceService.InspectProblemsAsync(
                goalId, GoalWorkspaceScope.ApprovedWorktree, new(path), cancellationToken);
            diagnostics.AddRange(result.Diagnostics);
            if (result.Issue is not null) findings.Add($"{path}: {result.Issue.Message.Value}");
        }

        if (git.Diff.Contains("TODO", StringComparison.OrdinalIgnoreCase) ||
            git.Diff.Contains("NotImplementedException", StringComparison.Ordinal))
            findings.Add("Changed source contains TODO or NotImplementedException placeholder text.");
        ToolEvidenceSnapshot evidence = await evidenceService.ListAsync(goalId.Value, cancellationToken);
        bool build = LatestSucceeded(evidence, ToolKind.Build);
        bool tests = LatestSucceeded(evidence, ToolKind.Test);
        if (!build) findings.Add("No successful Build evidence exists for the current goal.");
        if (!tests) findings.Add("No successful Test evidence exists for the current goal.");
        if (truncated) findings.Add($"More than {MaximumFiles} changed C# files require checking.");
        bool compilerErrors = diagnostics.Any(item => item.Severity is WorkbenchCodeDiagnosticSeverity.Error);
        ChangedSetQualityDisposition disposition = compilerErrors || findings.Any(item =>
                item.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
            ? ChangedSetQualityDisposition.Failed
            : build && tests && !truncated
                ? ChangedSetQualityDisposition.Passed
                : ChangedSetQualityDisposition.Incomplete;
        return new(identity.Identity, disposition, git.Changes.Count,
            Math.Min(changedCSharp.Length, MaximumFiles), diagnostics.Take(5_000).ToArray(),
            build, tests, findings, truncated || diagnostics.Count > 5_000, timeProvider.GetUtcNow());
    }

    private static bool LatestSucceeded(ToolEvidenceSnapshot snapshot, ToolKind kind) =>
        snapshot.Items.Where(item => item.Tool == kind).OrderByDescending(item => item.StartedAt)
            .FirstOrDefault()?.State is ToolEvidenceState.Succeeded;
}
