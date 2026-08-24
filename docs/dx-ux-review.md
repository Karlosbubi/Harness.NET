# DX and UX review: working in and on Harness.NET

This writeup collects measured experience improvements beyond the structural work
already delegated in [ADR 025](decisions/025-workbench-composition-and-refactor-guardrails.md)/
[Task 060](tasks/README.md#060--workbench-composition-refactor) and
[ADR 026](decisions/026-translation-boundary-and-architecture-enforcement.md)/
[Task 061](tasks/README.md#061--architecture-enforcement-and-composition-seams).
Findings are dated 2026-08-24 (branch `docs/task-060-refactor-baseline`); each names
its evidence. Items here are **candidates** — none is registered as a task. On
acceptance, promote selected items to numbered tasks (062+) with normal acceptance
criteria; several small ones can share one task.

Two audiences, two parts:

- **In** — the developer using Harness.NET as their IDE and agent workbench.
- **On** — the contributor (human or delegated agent) changing this repository.

Everything proposed stays inside the accepted ideals: local-first, typed contracts,
no web UI, no telemetry by default, no repository metadata, fiscally conservative
tests.

## Part I — working in Harness.NET

### U1. Transient event surface (notifications)

**Observation.** There is no notification or toast mechanism anywhere in the
Avalonia adapter (zero matches for either concept in
`src/Harness.Presentation.Avalonia`). Completion of long operations — agent runs,
semantic indexing, fetch/push, backup — is visible only in the panel that started
them. A developer working in the editor learns that a goal finished or a push was
rejected only by looking away at the right card or status text.

**Proposal.** A small typed event surface: bounded queue of `WorkbenchEvent`
records (severity, source, message, optional navigation target), rendered
non-intrusively, keyboard-dismissable, fully AT-SPI announced, never stealing
focus, persisted only for the session. Task 053's per-session attention states
should build on it rather than invent a second mechanism — this is groundwork worth
landing first.

**Fit.** Complements Task 053 (needs-direction/blocked states) and Task 052
(build/test completion). Presentation + one Business Logic contract; no new
authority.

### U2. Running spend visibility during agent execution

**Observation.** Remote cost is governed rigorously (reservation, reconciliation,
caps) but surfaces in the conversation only at failure ("The monetary cost limit
stopped the run…", `ConversationWorkflowCard.cs:444`) and in Settings/goal
settings. The Dashboard contracts contain no cost fields. During a running remote
goal there is no glanceable "reconciled so far / reserved / remaining cap" readout.

**Proposal.** A compact per-goal spend indicator on the active workflow card and
the running-goal list, fed by the existing `IRemoteCostService` reports —
reconciled, reserved, remaining (for `Capped`), provider/model on hover. Micro-USD
stays internal; USD at the boundary, per the framework rule. No new accounting.

**Fit.** Pure Presentation over existing contracts. Directly serves the
"Explicit authority" and "Auditability" principles — spending users can see is
spending they can stop.

### U3. Command palette fuzzy matching and recency

**Observation.** Palette scoring is substring/word-prefix based
(`CommandPaletteModel.cs:63–94`). `git wt` will not find "Git: Worktrees";
subsequence queries (`gwt`) find nothing. There is no recency or frequency
weighting, so a command used every session costs the same keystrokes as one never
used.

**Proposal.** Subsequence fuzzy matching with word-boundary bonuses (the current
scorer generalizes cleanly), plus a small session-persisted recency boost stored in
private state (SQLite preferences, not the repository). Deterministic and
unit-testable; extend `CommandPaletteFilterTests` with ranking cases.

**Fit.** Small, isolated, high daily-use payoff. Slots naturally into Task 060.8's
palette coverage audit or stands alone.

### U4. Keyboard reference overlay

**Observation.** Keybindings are typed, discoverable in Settings, and hinted in
headers, but there is no in-app cheat sheet: no help overlay and no `F1` handling
anywhere in the Avalonia adapter (zero matches). Learning the keyboard surface
requires opening Settings and reading a table.

**Proposal.** A read-only, searchable overlay (suggested default `F1` or
`Ctrl+K Ctrl+S` chord subject to conflict validation) rendering the live
`KeybindingCommandBindings` snapshot grouped by category, including Vim state notes
when Vim mode is active. Zero new state; renders existing contracts.

**Fit.** Pairs with ADR 021's discovery goals; trivial after Task 060.1's
composition seams.

### U5. Vim depth: named registers, search, macros

**Observation.** Vim mode covers modes, counted core motions/operators, and a
single unnamed register (`VimEditorController.cs:25–26`); there are no named
registers, no `/`/`?` search motions, and no macros. For a Vim-habituated
developer the missing search motion is the sharpest edge — `/` is a primary
navigation verb.

**Proposal.** In priority order: `/`, `?`, `n`, `N` incremental search as motions
(compose with operators, e.g. `d/foo`); named registers `"a`–`"z` plus `"+`
mapping to the existing clipboard register; macros (`q`, `@`) last — they multiply
every future binding's test matrix and deserve their own decision. Extend
`VimEditorControllerTests` per feature; keep the modal-input rules of ADR 021.

**Fit.** Contained in one controller + tests. Ship search alone if the rest waits.

### U6. Split editor groups

**Observation.** Documents open as dockable tabs with a switcher; diffs render
side-by-side (`UnifiedDiffDocument.ToSideBySideRows`), but there is no split-editor
command — two source files cannot be viewed side by side except by manual dock
dragging, and nothing preserves or names such an arrangement.

**Proposal.** Explicit "Split right / split down / move document to group"
commands over the existing Dock layout model and codec, persisted through the
ADR 011 layout state like every other arrangement. Defer until after Task 060.5
(DocumentsHost extraction) — doing it before means doing it twice.

**Fit.** Dock.Avalonia supports the layout topology already; the work is commands,
codec coverage, and tests.

## Part II — working on Harness.NET

### D1. Acceptance evidence integrity

**Observation.** `artifacts/` is git-ignored, yet acceptance records under
`docs/acceptance/` reference files inside it (2 documents today, e.g. screenshots
under `artifacts/usability/`). The referenced evidence exists on exactly one
machine. Anyone else — including a delegated agent asked to "re-verify the
acceptance record" — follows a link to nothing. For a repository whose working
agreement treats acceptance evidence as a completion gate, that evidence being
unversioned is a real integrity gap.

**Proposal.** Decide the evidence policy explicitly, then enforce it: either
(a) commit bounded, redaction-checked evidence into `docs/acceptance/evidence/`
(small PNGs and text logs; no secrets; size-budgeted), or (b) declare evidence
machine-local and rewrite acceptance records to describe rather than link. A
link-checker over `docs/` in the Task 060.0 verification workflow keeps it true
either way. Option (a) fits the auditability principle better and is recommended.

### D2. Test taxonomy and the fast inner loop

**Observation.** The suite (813 tests, ~36 s) has effectively no categorization —
5 `[Trait]` attributes total. The Roslyn/MSBuild-heavy `Harness.DataAccess.Tests`
(20 s) and the headless-Avalonia `Presentation.Avalonia.Tests` (13 s) dominate;
the 299 pure Business Logic tests finish in 2 s. There is no supported way to say
"run only the deterministic fast tier while I iterate."

**Proposal.** A three-trait taxonomy — `Tier=Fast` (pure, no I/O),
`Tier=Adapter` (Roslyn, LibGit2Sharp, SQLite, headless UI), `Tier=Live`
(existing opt-in) — applied mechanically, with documented
`dotnet test --filter` recipes in the README and used by the Task 060.0 workflow
to fail fast on the cheap tier before paying for the slow one. An xunit
parallelization config (`xunit.runner.json`, currently absent) belongs to the same
slice.

### D3. `eng/` script catalog

**Observation.** `eng/` holds 15 uncatalogued Python/Bash scripts (AT-SPI
verification, screenshot capture, publish gates, local-model regression) with no
README and no statement of which acceptance record each feeds, what environment it
needs (display, Ollama, portals), or how long it runs. This is tribal knowledge —
exactly the kind AGENTS.md tells agents not to rely on.

**Proposal.** `eng/README.md`: one table — script, purpose, prerequisites,
consumed by (task/acceptance record), typical duration. Keep it a directory
listing with meaning, not a build system; the typed-task work in Task 051 may later
subsume some of it, and the catalog is what makes that migration checkable.

### D4. Documentation entry point

**Observation.** `docs/` now holds 8 top-level documents plus decisions, tasks,
acceptance, and mockups, with no index. AGENTS.md step 1 says "read `README.md`,
`docs/framework.md`, and the relevant accepted decision records" — finding *which*
records are relevant is left to directory browsing.

**Proposal.** A short `docs/README.md` map: what each document answers, reading
order for a new contributor (vision → framework → architecture → roadmap), and
pointers into decisions/tasks/acceptance. Ten minutes of writing, recurring payoff
for every delegated agent that starts cold.

### D5. Dependency review cadence

**Observation.** Central package management is clean, but there is no update
cadence: no automation, no recorded review practice, and one preview package in
production dependencies (`Microsoft.SemanticKernel.Connectors.SqliteVec`
1.74.0-preview) with no recorded exit plan. `THIRD-PARTY-NOTICES.md` is maintained
by hand with nothing checking it against `Directory.Packages.props`.

**Proposal.** Lightweight and local-first, matching the project's posture: a
recorded monthly review step (`dotnet list package --outdated` +
`--vulnerable`, results noted in the task ledger when action is taken), a
decision note for the SqliteVec preview dependency naming its GA condition, and a
notices-vs-packages consistency check in the Task 060.0 verification workflow.
Bot-driven update PRs (Dependabot/Renovate) are *not* proposed — unreviewed
dependency churn fits this repository poorly.

### D6. Contributor on-ramp

**Observation.** README's build/run section is good and current except that it
states "SDK 10.0.201 is pinned" — which will be false the moment Task 060.0 lands
(and is the machine-portability bug today). There is no CONTRIBUTING.md; AGENTS.md
carries the working agreement but is addressed to agents; the branch/draft-PR/
evidence expectations a human contributor needs live across three documents.

**Proposal.** Fold into Task 060.0's documentation criterion: update the README
SDK sentence in the same slice as the pin change, and add a short CONTRIBUTING.md
that points at AGENTS.md as the working agreement, states the branch → draft PR →
evidence flow, and links D4's docs map. No duplicated rules — pointers only.

## Suggested packaging

| Candidate | Size | Natural home |
|---|---|---|
| D6 README/CONTRIBUTING, D1 link-checker, D5 notices check | S | Fold into Task 060.0 |
| U3 palette fuzzy+recency | S | Task 060.8 or standalone |
| U4 keyboard overlay | S | After 060.1, standalone |
| D2 test taxonomy + runner config | M | New task; do before CI grows |
| D3 `eng/` catalog + D4 docs map | S | One documentation task |
| U1 event surface | M | New task; before 053 |
| U2 spend visibility | S–M | New task; pure Presentation |
| U5 Vim search, then registers | M | New task; search first |
| U6 split editor groups | M | New task; after 060.5 |
| D1 evidence policy decision | S + ADR-lite | Needs an explicit user decision |

Recommended first wave: the three S-sized DX folds into 060.0, then D2 (it makes
every subsequent slice cheaper to verify), then U1/U2 (they compound with Tasks
052/053 rather than competing with them).

## Non-goals

Web or remote UI, always-on telemetry, unreviewed dependency automation, a general
plugin system, notification daemons or system-tray integration, and anything that
adds Harness metadata to user repositories. Each is either deferred by the roadmap
or excluded by the vision, and nothing above requires them.
