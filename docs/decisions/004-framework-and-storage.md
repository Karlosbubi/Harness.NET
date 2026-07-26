# ADR 004: Framework and storage ownership

- Status: Accepted
- Date: 2026-07-26

## Context

The framework must be durable and layered without adding product-specific clutter to
repositories or hiding shared engineering rules in an application database.

## Decision

Use Markdown for intent, typed configuration for enforceable policy, and skills for
procedures. Precedence is global user, repository guidance, private workspace
overlay, goal, task, then agent role. More-specific rules win unless an earlier rule
is locked; same-level conflicts pause for clarification.

Treat `AGENTS.md` and existing documentation as native shared repository guidance.
Do not create a `.harness` directory. Store global private framework data in XDG
configuration storage and operational/private workspace data in Harness.NET's
SQLite database.

Rule promotion always presents a diff and lets the user choose global private,
workspace private, `AGENTS.md`, or an existing documentation destination.

## Consequences

- User repositories contain only guidance they already use or explicitly approve.
- Private overlays and summaries are local and do not surprise collaborators.
- Shared rules remain reviewable through the repository's normal Git workflow.
- The application must explain rule provenance, precedence, and locks.

## Alternatives considered

- A repository `.harness` directory was rejected to keep user repositories clean.
- SQLite-only framework storage was rejected because shared guidance must remain
  human-readable and reviewable.
