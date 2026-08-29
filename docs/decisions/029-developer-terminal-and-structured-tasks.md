# ADR 029: Developer terminal and structured tasks

- Status: Accepted
- Date: 2026-08-29
- Extends: [ADR 005](005-isolated-goal-execution.md), [ADR 010](010-docked-desktop-workbench.md), [ADR 016](016-model-accessible-ide-capabilities.md), [ADR 023](023-typed-developer-dotnet-execution.md), [ADR 027](027-contributor-verification-and-dependency-governance.md)

## Context

Task 051 requires an interactive developer terminal and repeatable repository tasks.
Neither capability is the durable Run output surface, and neither may weaken the
closed, typed authority available to agents. Redirected process streams are not a
terminal: interactive shells and full-screen programs require a pseudo-terminal,
terminal emulation, resize propagation, process-group cleanup, and explicit handling
of potentially sensitive output.

Building a native PTY implementation and terminal emulator inside Harness.NET would
add a large platform and protocol maintenance burden. Letting a presentation control
own the child process would instead collapse lifecycle, trust, and persistence policy
into the UI layer. A generic command string shared by terminals, tasks, and agents
would also make their materially different authority impossible to audit.

## Decision

Harness.NET provides a trusted-workspace, developer-only terminal. Business Logic owns
session identity, source context, trust validation, bounds, lifecycle, restart state,
and persistence policy. Data Access owns shell resolution, PTY creation, byte I/O,
resize, exit observation, and process-tree stop. Presentation owns terminal rendering,
keyboard and pointer interaction, selection, copy/paste, search, links, tabs, and
developer confirmations. Only immutable records, enums, and interfaces cross those
boundaries; native handles, process identifiers, streams, and provider types do not.

The first supported adapter uses the exact centrally pinned `Porta.Pty` package in
Data Access and `SvcSystems.UI.Terminal` in Avalonia Presentation. Porta.Pty supplies
the native PTY/ConPTY boundary without performing managed work after `fork`, while the
model-driven SvcSystems control keeps process ownership outside Presentation. Both are
MIT licensed. Version changes require the normal dependency, vulnerability, publish,
and adapter evidence in ADR 027. Harness.NET does not expose either package contract
above its owning layer.

A terminal starts only for the exact trusted original workspace or approved goal
worktree resolved by the existing source-context boundary. Shell selection is a closed
platform policy resolved inside Data Access; Presentation and models cannot provide an
executable path. The interactive developer controls the shell after it starts. The
initial environment is inherited from Harness.NET with a small locked terminal policy,
and the UI describes that profile without exposing values.

Terminal bytes are untrusted and may contain credentials. Live content is bounded and
process-local by default. It is excluded from logs, backups, diagnostics, model
context, portal captures, and durable Run evidence. Private persistence stores only
safe session metadata and a terminal lifecycle; it never restores a process. Any
future optional scrollback persistence must be explicit, privately stored, bounded,
and independently reviewed for secret handling. Sessions left running at shutdown are
reconstructed as interrupted.

Stop and shutdown cancel I/O and terminate the complete owned process tree. Closing a
view is not allowed to orphan its process. No terminal service is registered in the
agent tool catalog, and agents cannot read terminal bytes, send input, resize a PTY,
or start a shell. A future model command remains a separate closed operation under ADR
016; developer consent to a terminal does not grant it.

Structured tasks are a separate Business Logic capability. A task definition contains
a typed executable, argument vector, confined working directory, bounded environment,
dependencies, presentation policy, cancellation policy, and closed problem matcher.
It never contains a shell command string. Discovery reads only supported existing
repository conventions and private Harness.NET settings; Harness.NET creates no
repository metadata directory. Task lifecycle and safe result metadata are separate
from terminal sessions, developer .NET execution, and goal tool evidence.

## Consequences

- Interactive programs receive a real PTY while trust and process ownership remain
  testable outside Presentation.
- Terminal content cannot silently become durable evidence or model context.
- Multiple terminal views can share one bounded lifecycle service without making UI
  controls responsible for processes.
- Structured tasks remain inspectable and repeatable without turning task names or
  configuration text into shell syntax.
- The two new packages add native/presentation supply-chain surface and therefore need
  exact pins, notices, adapter tests, vulnerability checks, and published-output checks.

## Alternatives considered

- Redirected standard streams were rejected because they do not implement PTY
  semantics, terminal resize, or interactive full-screen applications.
- `Iciclecreek.Avalonia.Terminal` was rejected because its control also owns process
  creation, crossing the Presentation and Data Access boundary.
- `XTerm.NET` alone was rejected because it would require Harness.NET to build and
  maintain its own Avalonia renderer despite an available model-driven adapter.
- A single generic shell-command runner was rejected because it conflates developer
  interaction, structured automation, typed agent authority, and durable evidence.
- Persisting live scrollback by default was rejected because terminals routinely
  display secrets and arbitrary repository content.
