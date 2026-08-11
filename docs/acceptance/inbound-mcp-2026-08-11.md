# Inbound MCP acceptance — 2026-08-11

Scope: Task 047 semantic IDE completion and Task 059 inbound MCP control/evaluation.

## Deterministic evidence

- `dotnet build Harness.slnx --no-restore -m:1 -p:UseSharedCompilation=false`
  completed with zero warnings and errors.
- `dotnet test Harness.slnx --no-restore -m:1 -p:UseSharedCompilation=false`
  passed all 594 deterministic tests.
- Data Access tests use the official MCP 2.x client against a real loopback stateless
  Streamable HTTP server. They verify bearer authentication, initialization
  instructions, closed discovery, read-only annotations, approval-held omission,
  invocation, active-client attribution, disconnect, live token rotation, restart,
  audit retention, settings validation, and fixture confinement/reset.
- Roslyn tests cover symbol search, incoming/outgoing calls, base/derived/override
  relationships, associated tests, paging, exact baseline sessions, and existing
  diagnostics/navigation/refactoring behavior.
- Business Logic tests verify role/module eligibility, one-next-turn activation,
  expiry, durable grant evidence, and safe saved exposure filtering.
- An actual `Harness.Host --no-ui --mcp-evaluation-root <temporary-root>` process
  initialized schema 25, created and registered only its deterministic fixture, then
  shut down cleanly. The temporary roots used by acceptance were removed afterward.

## Isolation and authority checks

- Evaluation roots must be dedicated descendants of the system temporary directory.
- Evaluation provider and inbound bearer secrets are process-local and volatile.
- Fixture reset validates its private root, performs a hard reset, removes only
  untracked files below that root, and leaves adjacent state unchanged.
- Normal mode cannot use evaluation-only UI activation, owned-frame, snapshot, or
  reset operations. An evaluation process cannot expose Normal mode state.
- Mutating tools require the current application-instance ID and reuse existing typed
  workspace, goal, trust, baseline, execution, capture, disclosure, and approval
  checks.
- No inbound schema accepts a shell, executable, SQL, generic tool/command name,
  coordinate, text-input target, credential value, or unrestricted path.

## Desktop evidence

`harness_ui` returns the closed accessibility action catalog, a bounded Avalonia
automation-element snapshot, exact open-buffer state, and layout-visible controls. In
an isolated process it additionally returns a bounded PNG rendered from the Harness
window itself. Normal mode never returns this owned frame; normal screenshots continue
through the XDG portal and its user consent.

The repository accessibility gate remains:

```bash
./eng/verify-avalonia-atspi.py
```

It passed against the production Avalonia frontend in the graphical Linux session.

The Linux publish gate remains:

```bash
./eng/verify-linux-x64-publish.sh
```

It produced and validated the self-contained Linux x64 application successfully.

## Client enrollment

1. Open Settings → Harness control.
2. Set a loopback endpoint, client/tool allowlists, and approval holds.
3. Enable the server and apply.
4. Select **Rotate and copy token once**.
5. Configure Streamable HTTP with the shown endpoint, the copied bearer token, and a
   stable `X-Harness-Client` header.

The token cannot be read again. Rotate it to enroll a replacement client or revoke
all existing clients.

## Normal-mode dogfood follow-up

A second Harness.NET checkout was run normally and connected as a stateless MCP
server while this checkout was the active workspace. The client used the exposed
workspace, Git, project-graph, goal, evidence, Roslyn diagnostics, document-read, and
UI-inspection tools. It did not receive shell or unrestricted process authority.

The run found and fixed these defects:

- The Git inspection adapter included the contents of untracked files in its bounded
  diff. An untracked client configuration therefore exposed its bearer token through
  `harness_git`. Git status still reports untracked paths, but the diff now contains
  tracked and staged changes only. A regression test proves that untracked content is
  absent while tracked edits remain visible. The exposed token must be rotated.
- Roslyn workspace diagnostics reported `AnalyzerReleases.Unshipped.md` twice because
  the analyzer project explicitly included a file already supplied by the analyzer
  SDK. The duplicate item was removed. A repeated `harness_code_problems` call returned
  no workspace issue or file diagnostics.
- An Ollama Lead returned no content, and another returned a task with an invalid
  field. Recovery previously exposed raw JSON parser text or one generic task error.
  Empty output now gives a direct model/guidance recovery action. Invalid tasks name
  the task index and invalid field.
- The Lead prompt said build/test evidence could replace mutation evidence, although
  the approved goal workflow requires each Implementer slice to mutate its isolated
  worktree. The prompt now states that validation belongs inside an implementation
  slice and rejects goals that explicitly prohibit source changes. The build boundary
  correctly refused an old goal without an active approved worktree grant; this safety
  check was retained.
- `.editorconfig` applied two-space indentation to C# even though the repository uses
  four spaces. A format verification therefore reported nearly every C# line as an
  error. C# now explicitly uses four spaces; MSBuild and data/document formats remain
  at two. The remaining real whitespace and import-order findings were formatted.
- Harness control policy, timeout, result-limit, and audit-retention fields exposed no
  accessible names. Exact automation names and a headless regression test now make
  those settings identifiable to both assistive technology and dogfood agents.
- `harness_goal_models` serialized all 412 discovered models in one response (about
  134,000 tokens), while `harness_goals` serialized every historical workflow and
  duplicated its prompt/evidence inside the active-operation view. That blocked later
  stateless requests while the oversized response was written. Goal, evidence, and
  model collection tools are now bounded and continuation-paged. Goals can be selected
  by exact ID; model filtering happens by provider, role, and search text on the server;
  goal summaries omit prompt/evidence duplication.
- Three mutating lifecycle policies disagreed with their protocol annotations and were
  displayed as idempotent in Settings. Configuration, model selection, and commit
  request are now consistently non-idempotent, so a caller will not replay them after
  an ambiguous timeout.
- After ornith failed plan validation and then returned no plan, the goal was rerouted
  to local Gemma 4. Gemma produced a valid plan but ignored corrective guidance by
  adding a standalone “Verify implementation” task. The deterministic normalizer let
  it through because the objective later used “change” as a noun. Validation-first
  titles such as Verify, Run, Execute, and Check are now discarded unless the title
  itself also names a mutation. The observed plan is covered by a regression test and
  was not approved.

The lifecycle dogfood run created a local-only goal over MCP, observed the expected
failure when its configured Lead route was remote, selected local `ornith:9b`, and
used the explicit retry operation first without and then with corrective guidance.
Planning returned an operation identity instead of holding the HTTP request. Durable
failure and retry checkpoints remained inspectable throughout. After the bounded
response fix was deployed, exact-goal polling completed without evidence duplication,
and provider/role-filtered model discovery returned three of eight matching Ollama
Lead models with continuation `3`.

Verification after the fixes:

- `dotnet build Harness.slnx --no-restore -m:1 -p:UseSharedCompilation=false`
  completed with zero warnings and errors.
- `dotnet test Harness.slnx --no-build --no-restore -m:1
  -p:UseSharedCompilation=false` passed all 598 deterministic tests.
- Focused Git-inspector tests passed 4/4. Focused Lead-delegation parser tests passed
  15/15.
- `dotnet format Harness.slnx --no-restore --verify-no-changes` completed without a
  finding.
