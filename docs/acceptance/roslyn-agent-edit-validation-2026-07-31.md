# Roslyn agent-edit validation acceptance — 2026-07-31

## Accepted behavior

- Human source saves remain responsive and baseline-protected without being blocked
  by incomplete code.
- The agent tool supplies model authority outside the model-visible arguments. A model
  cannot opt out of deterministic validation.
- A model-authored compiler-source replacement requires an approved goal worktree,
  trusted active workspace, exact existing-file SHA-256, and matching foreground
  Roslyn session.
- Roslyn applies candidate text only to an ephemeral solution and compares compiler
  plus configured-analyzer diagnostics across affected projects. Stable identities
  preserve existing findings across line shifts and preserve duplicate counts.
- An introduced compiler Error rejects the tool call before the atomic editor writes.
  Existing errors remain retained evidence and do not block unrelated improvements.
- Candidate and applied validation results are stored separately in the durable tool
  result, preserving introduced warning/analyzer evidence after the post-write check.
- The applied pass requires the persisted hash and candidate text to match before the
  live compiler state advances. A mismatch produces failed durable evidence even
  though the atomic write itself completed.
- Files outside the compiler workspace, such as Markdown documentation, receive an
  explicit `NotApplicable` disposition. Known project-system inputs fail closed while
  they cannot be represented honestly as an in-memory Roslyn document candidate.

## Deterministic checks

`RoslynCodeIntelligenceEngineTests` proves:

- introduced compiler errors are rejected without changing disk;
- pre-existing errors are retained while newly introduced warnings are reported;
- applied text mismatch is stale/failed and matching applied text advances state;
- unsupported files return `NotApplicable`;
- SDK/load, live diagnostics, baseline staleness, cancellation, and context replacement
  behavior remain intact.

`WorkspaceMutationServiceTests` proves:

- rejected model source edits never reach the file editor;
- accepted source edits run Candidate, atomic write, then Applied in order;
- candidate and applied results are both durable structured evidence;
- post-apply failure completes evidence as failed;
- documentation edits persist with explicit `NotApplicable` evidence;
- existing human mutation behavior remains unchanged.

Focused results on the acceptance machine:

- Roslyn engine: 9 passed;
- mutation boundary: 16 passed;
- Business Logic code-intelligence mapping: 11 passed;
- solution build: 0 warnings, 0 errors.
