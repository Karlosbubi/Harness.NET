# ADR 016: Model-accessible IDE capability catalog and authority

- Status: Accepted
- Date: 2026-08-10
- Extends: [ADR 005](005-isolated-goal-execution.md), [ADR 012](012-roslyn-code-intelligence.md), [ADR 013](013-chat-first-desktop-workflow.md), [ADR 015](015-stateless-mcp-connections.md)

## Context

Harness.NET's agents can inspect files, text, Git and .NET metadata; edit bounded
files; run Build/Test; retrieve semantic context; and preview/apply Roslyn rename.
The editor itself has richer Roslyn diagnostics, completion, symbol information and
navigation that are not yet available to model roles. The resulting split means an
agent working inside the IDE has less deterministic IDE assistance than the developer.

JetBrains Rider 2026.2 provides a useful breadth benchmark through its MCP server. A
live inventory on 2026-08-10 included project and solution analysis, bounded file and
search operations, call and type hierarchy, code quality, refactorings, run
configurations, tests, debugger control and inspection, Git status, database tools,
dotTrace analysis, Unity profiling, inspection-authoring facilities, screenshots, a
dynamic tool router, terminal execution, and Unreal Engine tools. Rider's published
catalog also documents notebook execution, which remains relevant to the target .NET
IDE surface even though that tool was absent from this live instance.

Copying that surface literally would violate settled Harness.NET decisions. A generic
terminal or dynamic execute-by-name tool would bypass typed authority. Injecting every
schema into every role call would waste context and make tool selection worse. Some
debugger evaluations, database queries, run configurations and profiler operations can
execute user or dependency code even when their output appears observational.

## Decision

### Product-owned capability catalog

Harness.NET will expose a first-class, product-owned IDE capability catalog to Lead,
Implementer and Reviewer. Rider's non-Unreal catalog is a breadth reference, not a
wire contract or runtime dependency. Built-in capabilities remain available when no
external MCP server or other IDE is installed.

Each capability has immutable identity, category, description, input/output schema,
role eligibility, source-context requirements, trust requirements, authority class,
availability state and implementation module. Business Logic owns catalog and role
policy. Data Access owns Roslyn, Git, process, debugger, database, profiler and other
SDK adapters. Presentation owns developer gestures and status, not capability rules.

External MCP tools remain a separately attributed source in the same discovery UX.
They cannot impersonate a built-in capability or inherit its authority.

### On-demand typed toolsets

Do not place the complete IDE catalog in every model prompt. A small bootstrap set is
always present: bounded file reading/search, workspace/project status, Git inspection,
semantic context, durable evidence, and toolset discovery/request.

A model may request one or more closed toolsets for a concrete next step. Business
Logic validates the request against role, goal phase, workspace trust, source context,
delegated file areas and current approvals. The next bounded model turn receives the
actual typed functions and schemas for the granted toolsets. Requesting a toolset does
not invoke it or grant additional authority. Toolsets expire at the role-call or task
boundary and are recorded in workflow evidence.

This two-stage mechanism provides router-like context savings without a generic
`execute_tool(name, arguments)` escape hatch. Every invocation still reaches a named,
typed operation with its own validation. There is no generic "execute arbitrary
Roslyn action" or dynamic SDK method tool.

### Capability and authority classes

- **Inspect:** bounded reads of trusted project state, source, metadata, diagnostics,
  symbols, Git, existing output, snapshots and explicitly configured data sources.
- **Transform preview:** deterministic, side-effect-free computation of exact proposed
  changes, conflicts, baselines and a stable fingerprint.
- **Workspace mutation:** create/edit/format/refactor through approved goal-worktree
  scope, exact baselines, atomic application and deterministic post-edit validation.
- **Repository execution:** build, test, run, debug, analyzer/generator, notebook and
  profiler actions only after workspace trust, with typed targets and bounded output.
- **External or sensitive access:** database connections, network/restore, process
  attach, memory dumps, screenshots and credentials retain their dedicated approval,
  privacy and secret boundaries.
- **Destructive/integration:** database mutation, debug state mutation, package change,
  Git commit and similar operations require an explicit typed decision over their
  concrete target; broad toolset enablement is never sufficient.

Read-only debugger evaluation is not assumed: property getters, expression evaluation
and debugger function evaluation may execute target code. Database query text is not
classified as safe by a model-generated SQL parser; genuinely read-only inspection
uses a database principal whose server permissions enforce that boundary.

### Deterministic-first IDE behavior

When an IDE/compiler service can answer or perform an operation, models request that
operation instead of recreating it in prose or text edits. This includes diagnostics,
symbol information, definitions/references, call/type hierarchy, formatting, imports,
rename, signature change, extract/move/safe-delete operations, test discovery and
post-edit quality checks.

Every multi-file or semantic transformation is preview-first. Apply recomputes and
checks context identity, source baselines, delegated areas and preview fingerprint,
writes atomically, and records diagnostic/diff evidence. Formatting and code cleanup
use repository settings. A model-authored patch still passes the same Roslyn candidate
validation as any other model edit.

### Supported breadth and exclusions

The target catalog covers:

- workspace, solution/project graph, readiness, dependencies and problems;
- bounded file tree, open-document context, ranged reads, glob/text/regex/symbol
  search, dependency source/metadata navigation and editor navigation requests;
- diagnostics, lint, quick/symbol information, definitions, references, call/type
  hierarchy, test association and post-edit quality checks;
- exact file creation/patching plus preview-first formatting, namespace/import cleanup,
  rename, signature change, extract, move and safe-delete refactorings;
- asynchronous build state, test discovery/selection, named launch configurations,
  bounded process lifecycle and structured output;
- debugger launch/attach, sessions, breakpoints/logpoints, stepping, threads, stacks,
  frame values and explicitly risk-classified evaluation/state mutation;
- VCS roots, status, diff and existing exact-fingerprint commit workflow;
- database connection/schema/object/query inspection, pagination/cancellation and
  separately approved connection or data mutations;
- local performance snapshot inspection, timeline/call-tree analysis, memory-dump
  analysis and relevant Unity/.NET profiling;
- .NET notebook/interactive execution and typed analyzer-authoring validation; and
- portal-mediated visual verification from Task 045.

Unreal Engine support is explicitly outside the Harness.NET roadmap. This excludes
both `ue_*` tools and Rider tools whose current behavior is Unreal-specific despite a
different name, such as asset/Blueprint hierarchy, tag, actor, viewport and engine
screenshot operations. A generic XDG-portal screenshot remains a separate desktop
capability and is not an Unreal exception.

An unrestricted terminal is also excluded. Common development commands become closed
run configurations or typed adapters. A future user-defined command module requires
its own executable identity, argument, environment, working-directory, approval and
output contracts; it is not a shell string.

### Settings and visibility

The first catalog implementation slice includes **Settings → Agent tools**. It shows
each built-in and external source, category, module health, role eligibility, default
direct/on-demand state, trust/approval class and unavailable reason. Users can disable
optional capabilities or choose direct versus on-demand exposure where policy permits.

Settings never weakens a required trust or approval boundary. Goal creation and the
conversation surface show goal-specific toolset requests and consequential approvals.
The run timeline records selected toolset, invocations, truncation, cancellation,
results and durable mutation/execution evidence.

## Consequences

- Models can eventually use the IDE's deterministic knowledge instead of falling back
  to text search, generated edits or shell commands.
- The prompt stays small even as the catalog grows.
- Tool breadth does not collapse distinct authority classes into one "brave mode".
- Existing Roslyn/editor services become shared product capabilities rather than a
  presentation-only implementation.
- Debugger, database, profiling, notebook and Unity support require separate vertical
  slices and adapters; documenting parity does not falsely mark them delivered.
- Capability configuration ships with Settings and observable status from its first
  implementation slice.

## Alternatives considered

- Mirroring Rider tool names and schemas was rejected because it would couple the
  product to another IDE and leak platform-specific authority choices.
- Exposing the full catalog on every role call was rejected because schema context is
  finite and irrelevant tools reduce model reliability.
- A universal dynamic executor was rejected because names and JSON arguments are not
  a sufficient authorization boundary.
- An unrestricted terminal with confirmation was rejected by ADR 005 and remains
  unnecessary for operations that can be represented as typed capabilities.
- Treating all debugger and SQL reads as harmless was rejected because evaluation and
  query execution can have side effects.
