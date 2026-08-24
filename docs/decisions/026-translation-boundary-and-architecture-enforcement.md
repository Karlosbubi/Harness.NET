# ADR 026: Translation boundary and architecture enforcement

- Status: Proposed
- Date: 2026-08-24
- Extends: [ADR 001](001-layered-feature-architecture.md), [ADR 007](007-semantic-contract-types.md), [ADR 025](025-workbench-composition-and-refactor-guardrails.md)

## Context

A measured architecture review (2026-08-24, recorded in
[architecture.md](../architecture.md)) found the layer rules holding with zero
exceptions: no public class in either runtime layer, no Presentation reference to
Data Access, and reference direction enforced semantically at error severity. It
also found three load-bearing conventions that exist only as practice:

1. **Translation boundary.** Business Logic re-defines Data Access contracts
   instead of re-exposing them — 27 record families exist in both layers under the
   same name. Nothing prevents a future Business Logic contract from leaking a Data
   Access type into its public surface; Presentation would receive a value it
   cannot legally name, and the analyzer would not notice. Six Business Logic
   contract files already import Data Access namespaces; spot checks show only
   internal use, but no mechanism proves it.
2. **Coupling topology.** Cross-feature coupling inside Business Logic is almost
   entirely value-contract coupling (`GoalId` in 78 files outside `Goals`) while
   service interfaces stay feature-local (`IGoalService` referenced laterally by
   3 files). This shape is why features stay independently deliverable, and it is
   entirely unenforced.
3. **Composition root.** `Program.cs` performs ~138 registrations in one linear
   537-line file with using-aliases already needed to disambiguate mirror-record
   names. Every feature slice edits this file (19 touches in the last 60 commits),
   making it a standing merge hotspot with no structural seams.

## Decision

### Translation boundary is a rule, not a habit

The public surface of Business Logic — public interfaces, records, enums, and every
type reachable through their member signatures — consists of Business Logic types,
BCL types, and Microsoft.Extensions abstractions only. Data Access types stop
inside Business Logic implementations. The existing mirror-record duplication is
the accepted cost of this boundary and is never "cleaned up" by re-exposing the
Data Access original.

A new analyzer rule `HARNESS003` (error severity, alongside `HARNESS001/002` in
`Harness.Analyzers`) enforces it: a public Business Logic symbol whose signature
mentions a `Harness.DataAccess` type is a compile error. The six files currently
importing Data Access namespaces in contract-bearing files are audited in the same
slice; any violation found is fixed by introducing the missing mirror type.

### Cross-feature service coupling is explicit

Within Business Logic, feature namespaces may freely share public records and
enums. A feature service interface consumed by another feature namespace is
declared in a single checked inventory asserted by an architecture test seeded
from today's measured state. Extending the inventory is a reviewed one-line change
with a stated reason — cheap, but visible and versioned; the test exists to make
new lateral service dependencies deliberate, not to forbid them.

### Composition root has feature seams

Host gains internal per-feature registration modules (for example
`HostModules.Goals`, `HostModules.CodeIntelligence`), each owning the registrations
of one feature area across both layers. `Program.cs` retains ordering,
configuration loading, run-mode resolution, and shutdown ownership, and stays at or
under 200 lines. Layer rules are unchanged — Host already references every layer;
this is file organization with a budget, enforced by the same size-budget
architecture test introduced in ADR 025.

### Documentation stays measured

`architecture.md` carries the module map, coupling topology, translation boundary,
and enforcement matrix, with measured figures dated. A rule listed there is either
enforced by a named mechanism or explicitly marked as a gap or convention. Adding
an unenforced rule to the framework without recording its gap status is a
documentation defect.

## Consequences

- The dominant existing patterns become guarantees; a regression in any of them is
  a build or test failure with a named rule instead of a review catch.
- `HARNESS003` slightly increases friction when Business Logic wants to pass a Data
  Access record through: the author must create a mirror record and mapping. That
  friction is the boundary working as intended.
- The service-inventory test adds a maintenance point; keeping it a one-line
  reviewed change bounds the cost.
- Host modules turn most feature-slice merges in `Program.cs` into single-module
  edits; the 200-line orchestrator budget prevents regrowth.
- Analyzer work (signature walking, generic arguments, tuples, nullable
  annotations) needs careful tests; `Harness.Analyzers.Tests` grows accordingly.
- No runtime behavior changes anywhere in this ADR.

## Alternatives considered

- **Extract the shared identifier kernel into its own project.** Rejected: the
  value types are stable and cheap to share via namespace; a new project adds
  reference-graph and analyzer surface for a naming benefit the two-tier
  convention already delivers.
- **Eliminate mirror records by moving shared records into Data Access.** Rejected:
  inverts ownership — Presentation-facing contracts would be defined below
  Business Logic, and Data Access contract changes would ripple straight to the
  UI. The duplication is the decoupling.
- **Generate mirrors or use implicit conversions.** Rejected: source generation
  hides the boundary it exists to make visible, and conversions reintroduce the
  coupling the mirrors break.
- **Forbid lateral service references outright instead of an inventory.** Rejected:
  a small number are legitimate (workflow orchestration consumes goal state); the
  inventory records them instead of pretending they do not exist.
- **Leave all three as convention.** Rejected: each is one inattentive merge away
  from silent erosion, and the cost of mechanical enforcement is one analyzer rule
  and two architecture tests.
