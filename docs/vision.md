# Product Vision

## Purpose

Harness.NET is a local-first AI collaboration application specifically for .NET
software development. It makes one developer's preferred libraries, architecture,
quality standards, and working process explicit and operational.

Harness.NET coordinates a lead agent and specialist agents across repository
inspection, planning, implementation, verification, review, and acceptance. It
optimizes for useful, inspectable outcomes rather than maximum autonomy.

## Product principles

- **User-owned framework:** preferences are inspectable, editable, layered, and
  promotable through user-approved diffs.
- **Local-first control:** source, prompts, execution policy, and operational state
  remain local unless a selected provider requires remote inference.
- **Human authority:** the user approves plans, remote-provider use, exceptional
  capabilities, budget extensions, and Git commits.
- **Workspace autonomy:** after plan approval, agents may use typed repository-local
  edit, build, test, and inspection tools without repeated prompts.
- **Provider boundaries:** Ollama and OpenRouter remain Data Access concerns; model
  payloads do not define Business Logic or Presentation contracts.
- **Observable work:** activity, decisions, tool results, usage, and evidence are
  correlated, persisted, and expandable without exposing secrets in telemetry.
- **Replaceable presentation:** the first interface is a full-screen TUI. Avalonia
  applications and APIs such as gRPC can be added without moving business behavior.
- **No web frontend:** web-based presentation is outside the intended architecture.
- **Clean repositories:** Harness.NET does not add a custom metadata directory to
  user repositories.

## First complete workflow

1. Register and trust a Git-backed .NET repository.
2. Select a solution or project entry point and build its semantic index.
3. Create a goal, select models per role, and set review and remote-cost limits.
4. Let the lead inspect the repository and propose a plan.
5. Approve, revise, or reject the plan.
6. Create an isolated goal branch and worktree after approval.
7. Let the implementer edit and verify through typed tools.
8. Let an independent reviewer inspect the diff and evidence.
9. Repeat within the configured review-cycle limit or pause for user direction.
10. Inspect the outcome and explicitly approve a commit on the goal branch.

## Current implementation boundary

Harness.NET now includes enforced layer boundaries, XDG storage, additive SQLite
migrations, local observability, an adaptive Terminal.Gui shell, Ollama and
cost-controlled OpenRouter providers, semantic retrieval, durable goals and plans,
isolated worktrees, and role-scoped typed tools. A goal-bound production coordinator
runs Lead planning, pauses for plan approval, resumes Implementer work, and invokes an
independent Reviewer with durable expandable evidence. It checkpoints before model
calls, resumes completed safe boundaries, reconciles an already-durable plan, and
never automatically replays an uncertain call. Lead plans persist ordered bounded
tasks with file areas and acceptance criteria; Implementer calls execute those tasks
one at a time before bounded review/revision cycles and exact commit approval.
Remaining v1 work centers on production context assembly and operational hardening
before an Avalonia adapter begins.
