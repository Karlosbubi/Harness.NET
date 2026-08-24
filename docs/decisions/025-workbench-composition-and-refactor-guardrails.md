# ADR 025: Workbench composition and refactoring guardrails

- Status: Proposed
- Date: 2026-08-24
- Extends: [ADR 001](001-layered-feature-architecture.md), [ADR 009](009-avalonia-presentation-toolkit.md), [ADR 010](010-docked-desktop-workbench.md), [ADR 011](011-private-workbench-layout-state.md), [ADR 021](021-typed-keybindings-and-modal-input.md)

## Context

The runtime layers are healthy: the architecture analyzer and reference tests hold,
warnings are errors, and Business Logic contracts already cover every workbench
capability. The strain is inside the Avalonia Presentation layer. As of 2026-08-24,
`WorkbenchDockHost` is 6,389 lines with 85 fields, roughly 228 members, and 19 nested
types, and it appeared in 30 of the last 60 commits. `AvaloniaPresentationStore`
(2,606 lines, 20+ injected services), `SettingsWindow` (2,335), and `GoalDialog`
(2,275) show the same accretion. `PresentationControlTests` mirrors the problem as a
single 5,523-line test class. Every Stage 3 task (051–058) adds more workbench tools
to these same files.

Developer experience has two verified hazards: `global.json` pins SDK `10.0.201`
with `rollForward: latestPatch`, which fails outright on a machine with only
`10.0.400`, and repository verification runs only on developer machines because no
continuous verification exists. Untracked local agent configuration (`.codex/`)
holding live credentials sits outside `.gitignore`.

The exact measurements, decomposition targets, and delegation slices are recorded in
[the refactor baseline](../refactor-baseline.md). This record fixes the constraints
any refactor slice must observe.

## Decision

### Composition units

The workbench decomposes into per-tool composition units inside
`Harness.Presentation.Avalonia`. One unit owns one dockable tool or dialog section:
its controls, event wiring, and rendering of Business Logic contracts. Units are
internal sealed classes composed by a thin `WorkbenchDockHost` that retains only
dock arrangement, layout persistence, and cross-tool navigation. Units communicate
through the existing typed shell state and Business Logic contracts, never by
reaching into another unit's controls.

The decomposition stays inside the accepted idiom: Avalonia with code-first
controls, the Rx `BehaviorSubject` store, and typed contracts. No MVVM framework,
source-generated view models, XAML migration, or new UI dependency is introduced.
The `DataAccess -> BusinessLogic -> Presentation` direction, the analyzer, and the
reference tests are unchanged.

### Store decomposition

`AvaloniaPresentationStore` splits into feature-scoped internal command handlers
behind the existing single `AvaloniaShellState` stream. The observable state
contract, command gating, and cancellation ownership remain; only the internal
organization changes. State reduction stays in the store; composition units do not
mutate shared state directly.

### Size budget

A refactored presentation source file stays at or under 800 lines, and a test class
covers one composition unit. The budget is enforced by an architecture test with an
explicit burn-down allowlist naming each legacy file over budget; the allowlist may
only shrink. New files never enter the allowlist.

### Behavior preservation

Every refactor slice preserves, and proves with existing evidence:

- dock identifiers and serialized layout compatibility under ADR 011 — a layout
  saved before a slice restores identically after it;
- automation identifiers, AT-SPI structure, and the accepted `eng/` verification
  scripts;
- keybinding, command palette, and Vim behavior under ADR 021;
- every acceptance record listed for the touched surface.

A slice that must change an identifier records the migration in its task evidence
and updates the layout codec compatibility path in the same slice.

### Developer-experience baseline

- `global.json` states the supported major SDK and rolls forward within it
  (`latestFeature`); an exact SDK is pinned only where reproducibility requires it.
- Every pull request runs restore, build with warnings as errors, and the full
  deterministic test suite on Linux x64 through hosted continuous verification.
  Live-provider and paid tests stay excluded, consistent with the fiscally
  conservative testing rule.
- Local agent working directories (`.codex/` and equivalents) are ignored by Git.
  Credentials never depend on developer restraint alone.

### Scope limits

The refactor changes structure, not behavior, and does not: move logic across
runtime layers, add or replace UI toolkits, alter Business Logic or Data Access
contracts, introduce repository metadata, change the Terminal.Gui adapter beyond
compilation, or deliver new features in a refactor slice.

## Consequences

- Stage 3 tasks (051–058) land tools as new composition units instead of growing
  `WorkbenchDockHost`; parallel work on different tools stops colliding in one file.
- The size-budget architecture test makes structural drift a build failure instead
  of a review opinion.
- Refactor slices are individually reviewable and delegable; each is bounded by the
  behavior-preservation gates above.
- Continuous verification catches environment-dependent breakage (such as the
  current SDK-pin failure mode) before merge.
- The burn-down allowlist is temporary debt made visible; it must reach empty before
  the refactor task closes.
- Splitting the store and host is churn-heavy while Task 052 is in progress; slices
  must sequence around in-flight work as the baseline document specifies.

## Alternatives considered

- **Adopt an MVVM framework (ReactiveUI, CommunityToolkit).** Rejected: replaces a
  working typed Rx idiom with a dependency and relearning cost, contradicts ADR 009's
  minimal-toolkit stance, and does not itself fix file-level accretion.
- **Rewrite the workbench in XAML views.** Rejected: code-first controls are
  accepted practice here, are testable headless, and the problem is composition, not
  markup.
- **Leave structure as is and rely on review discipline.** Rejected: churn evidence
  (30 of the last 60 commits touching one file) shows the cost is already recurring,
  and Stage 3 adds more tools to the same files.
- **Split `Harness.Presentation.Avalonia` into multiple projects.** Rejected for
  now: project boundaries add analyzer and reference-test surface without helping
  cohesion; folders and composition units suffice until a measured need appears.
- **Self-hosted continuous verification.** Deferred: hosted Linux x64 runners cover
  the deterministic suite today; a self-hosted runner becomes relevant only with
  portal or display-dependent acceptance automation.
