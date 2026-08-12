# Morgania editor evaluation — 2026-08-12

Task: 048. This record covers the dependency gate and the first Linux source check.
It does not approve a Morgania migration.

## Inspected revisions

- RoslynPad tag `22.1`, commit
  `b62fdd819d6d1dd647b9999b6f34b47e1205c2d8`, dated 2026-08-04.
- Roslyn submodule commit
  `c0573ed0a7dc3e3b4d2e70da47f97cc51a35524f`, dated 2026-05-12.
- `vendor/vs-editor-api` is a copied tree, not a submodule. Its first repository commit
  is `c35da2735a0345b709d7ec39cf880a25db0763a4`; later RoslynPad commits modify it.
  The tree has no file that identifies the exact Microsoft upstream revision.

The RoslynPad and copied `vs-editor-api` license files are MIT. The Roslyn submodule
also carries its MIT license. No separate Morgania support or compatibility policy
was present in the inspected repository.

## Package and dependency gate

The upstream package guide names these packages:

- `Morgania.Editor.Abstractions`
- `Morgania.Editor`
- `Morgania.CodeAnalysis.EditorFeatures`
- `Morgania.CodeAnalysis.Editor`

On 2026-08-12, `dotnet package search Morgania` returned no packages from NuGet.org.
The official NuGet flat-container endpoint returned HTTP 404 for every ID. A source
checkout is therefore the only available route for this revision.

The source route is not a small editor control:

- 1,192 C# files are copied under `vendor/vs-editor-api`;
- 766 C# files exist in the Roslyn EditorFeatures trees selected by the project;
- 108 C# files are owned by the four Morgania projects;
- four projects are required for the Roslyn editor path;
- the complete demo restore reported 47 packages, while `RestoreHelper` reported 100;
- `RestoreHelper` uses three Azure DevOps feeds as well as NuGet.org;
- private-build `Microsoft.CodeAnalysis.LanguageServer.Protocol` and
  `Microsoft.CodeAnalysis.Remote.Workspaces` assemblies at
  `5.6.0-2.26263.10` are copied into output packages;
- the restore helper suppresses `NU1603`, `NU1605`, `NU1902`, and `NU1903`;
- `IgnoresAccessChecksToGenerator` opens internals in public and private Roslyn
  assemblies;
- three Roslyn source files are rewritten during the build to widen member access;
- the vendored editor projects disable nullable analysis, and the abstractions and
  EditorFeatures projects also disable normal analyzer coverage;
- the source requires Avalonia 12.1.1, Roslyn 5.6.0, and System.Composition 10.0.10.

Harness.NET currently resolves Avalonia 12.1.0, Roslyn 5.3.0, and
System.Composition 9.0.0. Adding Morgania would force a coordinated compiler,
composition, and UI upgrade. It would also add private Roslyn binaries to the SBOM.
That change is not an editor-only package replacement.

## Linux source results

The checkout requests .NET SDK 10.0.302. That SDK was not installed. The source was
restored and built by invoking the installed 10.0.201 MSBuild directly; this is a
compatibility probe, not an upstream-conforming release build.

Results:

- the Roslyn editor demo built with zero warnings and errors in 33.41 seconds wall
  time, with 814,128 KiB maximum resident memory;
- Morgania BehaviorTests passed 41/41;
- CompositionTests passed 4/4;
- GeometryTests passed 11/11;
- IntellisenseTests passed 14/14;
- the extension-conformance project built;
- the demo's own `--smoke` test failed twice after classification, completion, and
  Return-key checks passed;
- both failures were at brace completion: typing `(` after `Name.ToString` did not
  insert `)`;
- the first smoke process used 357,748 KiB maximum resident memory;
- the demo output directory occupied 610 MiB before publish trimming or a
  Harness-specific comparison.

The focused suites are useful evidence, but they do not cover Harness.NET's shared
buffer, Dock restoration, exact-baseline save conflicts, model tools, Wayland/X11
input, IME, Orca, AT-SPI, multiple displays, repeated workspace switches, or publish.

## Dogfood observations

Harness.NET was restarted from the synchronized live clone and used through its
stateless MCP endpoint.

- The MCP tool connector already loaded by the resumed Codex session returned HTTP
  401 after restart. A fresh client using the current repository-private connection
  credential succeeded. Restart continuity is fragile for clients that cache the
  authorization header.
- Creating a local-only Task 048 goal and selecting `Ollama/mistral-nemo:latest`
  worked through MCP.
- Lead planning took about 2 minutes 22 seconds.
- The delegation validator correctly rejected the plan because it contained
  standalone inspection or validation work despite an explicit prompt prohibition.
- The recovery state exposed the rejection reason but not the bounded rejected task
  list, which makes prompt and model diagnosis harder.

No paid inference was used.

The dogfood finding produced one small workflow fix. Invalid initial and retried Lead
responses now create private recovery evidence containing the parser or policy error
and at most 16 KiB of the rejected response. This gives the user enough evidence to
change the model or prompt without placing unbounded model output in workflow state.

## Harness adapter slice

Harness.NET now owns `IWorkbenchEditorAdapter` in Presentation. The current
AvaloniaEdit implementation contains the live text buffer, selection, caret and
offset conversion, user input events, pointer-to-source mapping, text changes,
diagnostic rendering, focus, theme updates, and disposal. The workbench and source
document session use this boundary for editing, dirty tracking, exact-baseline save,
conflict reload, diagnostics, completion commits, navigation, rename snapshots, and
restoration of already-open Dock documents.

Business Logic contracts and Roslyn buffer versions did not change. User edits and
model operations therefore still use the same text snapshot and stale-result checks.
The production editor remains AvaloniaEdit, so there is no second buffer or editor
stack.

AvaloniaEdit's completion and insight window constructors still require its native
`TextArea`. That escape hatch is confined to Presentation and is recorded explicitly;
a replacement adapter must own equivalent popup hosts before a cutover. This is one
of the remaining migration costs, not evidence that the editors are interchangeable.

Verification after introducing the seam:

- `dotnet build Harness.slnx --no-restore -p:UseSharedCompilation=false -m:1`
  succeeded with zero warnings and errors;
- `Harness.Presentation.Avalonia.Tests` passed 124/124, including editable open,
  dirty/save/conflict behavior, diagnostics, completion, signature help, quick info,
  navigation, rename, Dock switching, and layout restoration;
- focused workflow recovery tests passed, including bounded rejected-output
  preservation;
- the complete solution test run passed 618/618;
- `eng/verify-editor-intelligence.py` passed its Roslyn adapter, semantic boundary,
  editor control, stale-result, accessibility-name, and theme checks;
- `eng/verify-avalonia-atspi.py` passed against the production Avalonia surface;
- `eng/verify-linux-x64-publish.sh` produced and validated the self-contained Linux
  x64 application.

The existing real-workspace Roslyn performance acceptance was rerun after the seam.
It reported an 8,250 ms cold solution load, 2,122 ms warm diagnostic update, 35.3 ms
warm completion p95 over 20 requests, 25.0 ms definition navigation, 1.3 ms cancelled
request observation, and 108.3 MiB retained managed memory. The completion result was
Ready with no issues. These are current-adapter baseline values, not a Morgania
comparison; the rejected Morgania source cannot supply a production-equivalent slice.

## Gate disposition

| Task 048 gate | Result |
| --- | --- |
| Revision, license, package, support, integrity, provenance, SBOM | Inspected; failed public package, exact copied-source provenance, support, and private-binary SBOM gates. |
| Roslyn internals, MEF, warnings, nullable, upgrade work | Recorded; three source patches, internal-access bypasses, suppressed package warnings, disabled nullable/analyzers, and coordinated version upgrades make maintenance unacceptable. |
| Architecture boundary and decision | Passed for the retained implementation through ADR 020 and `IWorkbenchEditorAdapter`; no Morgania types or packages were added. |
| Representative editor behavior | Existing AvaloniaEdit behavior remains covered by the 124-test Presentation suite. A Morgania slice was not admitted after the dependency gate failed. |
| Shared buffer and stale results | Preserved and verified by the editor-intelligence gate; no duplicate workspace or buffer was introduced. |
| Linux input, accessibility, layout, publish | Current production surface passed headless interaction, Dock restoration, AT-SPI, and Linux x64 publish checks. Morgania comparison stopped at the failed upstream brace-completion smoke test and inadmissible dependency gate. |
| Performance and lifecycle | Current Roslyn latency and retained-memory baseline was measured. Morgania source build/process measurements were recorded but are not production-equivalent. |
| Maintenance benefit | Failed. The source closure and private Roslyn patches are substantially larger and riskier than the retained adapter. |
| Cutover and rollback | No cutover occurred. AvaloniaEdit remains the only production editor, so rollback is immediate and no user-state migration exists. |

## Current decision

Do not adopt the inspected Morgania revision. It fails the public-package,
provenance, version-coupling, and upstream-smoke gates before Harness-specific Linux
acceptance begins. Keep AvaloniaEdit and add a Presentation-owned adapter seam.

Re-run the Morgania comparison only when an admissible pinned release exists. The
remaining evidence must compare the replacement against this adapter for Wayland and
X11 input and accessibility, self-contained publish, startup and interaction latency,
memory, disposal, repeated source switches, popup ownership, and maintenance cost.
