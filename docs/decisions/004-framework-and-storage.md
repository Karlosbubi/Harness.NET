# ADR 004: Framework and storage ownership

- Status: Accepted
- Date: 2026-07-26

## Context

Engineering rules need clear ownership, precedence, and storage.

## Decision

Use Markdown for intent, typed configuration for enforceable policy, and skills for
procedures. Precedence is global user, repository guidance, private workspace
overlay, goal, task, then agent role. More-specific rules win unless an earlier rule
is locked; same-level conflicts pause for clarification.

Treat `AGENTS.md` and existing documentation as native shared repository guidance.
Do not create a `.harness` directory. Store global private framework data in XDG
configuration storage and operational/private workspace data in Harness.NET's
SQLite database.

Promotion shows a diff and requires the user to choose global private storage,
workspace private storage, `AGENTS.md`, or an existing documentation file.

## Consequences

- User repositories contain only guidance they already use or explicitly approve.
- Private overlays and summaries remain local.
- Shared rules remain reviewable through the repository's normal Git workflow.
- The application must explain rule provenance, precedence, and locks.

## Alternatives considered

- A `.harness` directory adds product-specific repository state.
- SQLite-only storage hides shared rules from normal review.
