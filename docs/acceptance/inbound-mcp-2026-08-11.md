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
