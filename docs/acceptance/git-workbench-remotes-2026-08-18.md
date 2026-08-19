# Git workbench remote synchronization acceptance

Date: 2026-08-18

This record closes Task 050's remaining remote-synchronization work under ADR 024.

## Delivered behavior

- The original-workspace Git workbench lists configured remotes with sanitized URLs,
  the current branch and upstream, exact local and remote-tracking commits, and
  ahead/behind divergence.
- Fetch names an exact remote source and local remote-tracking destination. It performs
  no working-tree integration.
- Integration is a distinct post-fetch preview. Fast-forward merge is the default;
  rebase is explicit, and dirty buffers or working state block integration.
- Push names exact local and remote branches. Non-forced push is the default.
  Force-with-lease is the only force option and requires a displayed remote-tracking
  commit; unconditional force is unavailable.
- Preview identity binds the complete Git fingerprint, operation, remote, refs, local
  commit, remote-tracking commit, divergence, and force policy. Apply recomputes the
  preview and rejects stale state before starting Git.
- Git runs through a closed argument-list adapter with terminal prompts disabled,
  bounded lifetime, cancellation, and process-tree cleanup. Configured credential
  helpers and SSH agents remain below Business Logic. Output is drained without being
  retained, and user information, query values, and fragments are removed from HTTP
  remote URLs before display.
- Remote actions are developer-only, original-workspace-only, and do not extend goal,
  agent, commit-approval, or model authority.

## Deterministic coverage

- Business Logic tests cover original-context enforcement, exact preview identity,
  observed commits, and force-with-lease mapping.
- Data Access fixtures use a local bare repository to cover fetch, reviewed
  fast-forward integration, push, exact observed-commit binding, and URL sanitization
  without external network access or credentials.
- Avalonia controls expose accessible names for remote/ref inputs, divergence status,
  policy choices, preview actions, and confirmation acknowledgement.

## Verification

- `dotnet build Harness.slnx --no-restore`: passed with zero warnings and errors.
- `DeveloperGitServiceTests`: 19 passed.
- `LibGitDeveloperGitRepositoryTests`: 44 passed.
- `PresentationControlTests`: 98 passed.
- Full `dotnet test Harness.slnx --no-build --no-restore`: 813 passed, zero failed
  or skipped.
