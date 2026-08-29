using System.Runtime.CompilerServices;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mutations;
using Harness.DataAccess.Workflows;
using StoredActor = Harness.DataAccess.Workflows.WorkflowActor;
using StoredKind = Harness.DataAccess.Workflows.GoalWorkflowCheckpointKind;
using StoredRunId = Harness.DataAccess.Workflows.GoalWorkflowRunId;
using StoredState = Harness.DataAccess.Workflows.GoalWorkflowRunState;

namespace Harness.BusinessLogic.Workflows;

internal sealed partial class GoalWorkflowService
{
    private static string LeadTask(GoalView goal) => $$"""
        Inspect the trusted workspace with your read-only typed tools before answering. At minimum,
        call inspect_dotnet and inspect the relevant existing source/test paths with read_file or
        search_text. Use get_symbol_info, find_symbol_definition, find_symbol_references, and
        inspect_code_problems wherever semantic code relationships affect the work. Then propose a
        bounded, verifiable implementation plan for this goal.

        Goal: {{goal.Title}}
        Objective: {{goal.Objective}}

        Return JSON only with exactly this shape (one surrounding ```json fence is tolerated but
        unnecessary). Supply 1-12 ordered tasks. Order tasks so each
        completed prefix is coherent, useful, and verifiable if a monetary cost limit stops later
        work: establish the smallest end-to-end foundation first, then add value in independently
        shippable increments. Each task must be bounded and define objective acceptance criteria.
        Do not create standalone discovery, inspection, planning, validation, build/test, or
        status-report tasks. Fold inspection and validation into an implementation slice. Every
        delegated task must produce durable successful mutation evidence, then build/test evidence
        where relevant. A goal that explicitly forbids source changes cannot enter this
        mutation-oriented workflow; report that conflict instead of inventing validation-only work.
        File areas are mutation grants: name only exact existing repository-relative files or
        directories that you observed, unless the goal explicitly authorizes creating a path.
        Prefer the smallest observed directory that contains all files for a slice. If the goal says
        to edit only existing files, never propose a new filename. Preserve exact public APIs,
        indexing conventions, validation commands, and prohibitions from the objective in the
        relevant delegated task rather than paraphrasing them away. Put optional polish last. Include explicit
        partial-completion checkpoints, verification, risks, and non-goals in the plan. Do not
        implement or claim that work is complete.

        {
          "plan": "reviewable plan including verification, risks, and non-goals",
          "tasks": [
            {
              "title": "bounded task title",
              "objective": "one independently implementable outcome",
              "fileAreas": ["relative/path/or/component"],
              "acceptanceCriteria": ["specific verifiable criterion"]
            }
          ]
        }
        """;

    private static string ImplementerTask(
        GoalView goal,
        PlanView plan,
        StoredGoalWorkflowTask task,
        int taskCount) => $$"""
        Implement only delegated task {{task.Sequence.Value}}/{{taskCount}} for goal
        '{{goal.Title}}' using typed goal-worktree tools. Respect its file-area boundary;
        inspect each exact existing target with read_file before editing and pass the returned
        sha256 as expectedSha256 to apply_file_edit. Never invent a path or baseline. If a tool
        rejects a request, use its error as evidence, inspect the workspace, and correct the request
        with a new correlation identifier. Before writing a call to an existing API, verify its exact
        signature and accessibility with get_symbol_info plus find_symbol_definition; use
        find_symbol_references or find_symbol_implementations when changing shared behavior or an
        abstraction. Treat those Roslyn results as source of truth rather than guessing from names.
        Use preview_symbol_rename/apply_symbol_rename for symbol renames. For compiler fixes and local
        refactorings, call find_code_actions and preview/apply its returned action rather than rewriting
        working code. Use inspect_code_problems around compiler-relevant edits. On a rejection or failed test,
        preserve passing code and
        repair only the cited diagnostic range or first relevant user-code stack frame; do not
        regenerate unrelated methods. Use atomic edits, then run the
        narrowest relevant build and tests without restore. Do not broaden scope or claim
        success without durable tool evidence. Work in small durable increments. Before broadening
        the change, leave the current increment coherent and run its narrow validation. If the
        provider or cost boundary stops the call, do not fabricate completion: preserve the last
        verified state and report completed criteria, validation, and remaining work separately.

        FULL GOAL OBJECTIVE (AUTHORITATIVE)
        {{goal.Objective}}

        APPROVED PLAN
        {{plan.Content}}

        DELEGATED TASK
        Title: {{task.Title.Value}}
        Objective: {{task.Objective.Value}}
        File areas:
        {{task.FileAreas.Value}}
        Acceptance criteria:
        {{task.AcceptanceCriteria.Value}}
        """;

    private static string ImplementationSummary(
        IReadOnlyList<StoredGoalWorkflowTask> tasks) => string.Join(
        "\n\n",
        tasks.OrderBy(task => task.Sequence.Value).Select(task =>
        {
            const int maximumReportCharacters = 4_000;
            string report = task.Report?.Value ?? "No durable report.";
            if (report.Length > maximumReportCharacters)
            {
                report = report[..maximumReportCharacters] +
                    "\n[report abbreviated; inspect durable task and tool evidence]";
            }

            return $"Task {task.Sequence.Value}: {task.Title.Value}\n{report}";
        }));

    private static IReadOnlyList<AgentFileArea> FileAreas(
        StoredGoalWorkflowTask task) => task.FileAreas.Value
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => new AgentFileArea(value.StartsWith("- ", StringComparison.Ordinal)
            ? value[2..].Trim()
            : value))
        .ToArray();

    private static IReadOnlyList<AgentFileArea> FileAreas(
        IReadOnlyList<StoredGoalWorkflowTask> tasks) => tasks
        .SelectMany(FileAreas)
        .Distinct()
        .ToArray();

    private static string ReviewerTask(
        GoalView goal,
        PlanView plan,
        AgentOutput implementation) => $$"""
        Independently review the approved goal worktree. Use inspect_git and list_tool_evidence;
        inspect relevant files and Roslyn problems, symbols, definitions, and references as needed.
        Check correctness, regressions, architecture, tests, and unsupported completion claims
        against the approved plan.

        GOAL: {{goal.Title}}
        FULL GOAL OBJECTIVE:
        {{goal.Objective}}

        APPROVED PLAN:
        {{plan.Content}}

        IMPLEMENTER REPORT:
        {{implementation.Value}}

        Return JSON only in one of these exact shapes:
        {"decision":"accept","summary":"specific evidence-based rationale"}
        {"decision":"revise","summary":"specific evidence-based rationale"}
        """;

    private static string RevisionTask(
        GoalView goal,
        PlanView plan,
        AgentOutput review) => $$"""
        Correct only the concrete findings from the independent review for goal
        '{{goal.Title}}'. Use only typed goal-worktree tools, inspect before editing, preserve
        the approved plan's scope, and build and test without restore. Do not claim success
        without durable tool evidence.

        FULL GOAL OBJECTIVE (AUTHORITATIVE)
        {{goal.Objective}}

        APPROVED PLAN
        {{plan.Content}}

        REVIEW FINDINGS
        {{review.Value}}
        """;

}
