# NetPad and OmniSharp parity

This matrix tracks Task 049 against these fixed reference revisions:

- NetPad `0c74746daf6f5402ad4d9a2cf3958131bdfc8011`;
- OmniSharp Roslyn `83fd615eafff33e297a9f59280d929cf09ec0d3c`.

The revisions are comparison sources, not runtime dependencies. Harness.NET keeps
one in-process Roslyn workspace and its own typed contracts. No NetPad or OmniSharp
source is copied by the delivered rows below.

Status meanings:

- **Delivered:** the developer workflow and shared typed service exist end to end.
- **Partial:** a useful subset exists, but Task 049 still has an explicit gap.
- **Planned:** no complete Task 049 slice exists.
- **Task 047:** the model-facing semantic foundation is already delivered there.
- **Excluded:** the feature does not belong in Harness.NET.
- **Conditional:** adoption requires measurements and an ADR change.

| Capability | NetPad reference | OmniSharp reference | Harness.NET status | Harness.NET evidence or remaining work |
|---|---|---|---|---|
| Completion and commit | OmniSharp completion provider | Completion service | Delivered | Roslyn completion list and exact-buffer commit contracts |
| Quick info and signature help | OmniSharp providers | Intellisense and signature services | Delivered | Accessible AvaloniaEdit insight windows |
| Diagnostics | OmniSharp diagnostics events | Diagnostics services | Delivered | Exact-buffer compiler/analyzer problems and editor adornments |
| Definition, usages, implementations | OmniSharp feature queries | Navigation services | Delivered | Developer actions and bounded typed destinations |
| Semantic rename | Rename query | Refactoring service | Delivered | Fingerprinted multi-file preview/apply; no direct write |
| Semantic classification | Semantic-highlighting provider | Semantic-highlight service | Delivered | Visible-range Roslyn classification with stale-result rejection and theme tokens |
| Occurrence highlighting | Document-highlight provider | Highlighting service | Delivered | Definition/read/write occurrences from the exact live buffer |
| Folding | Block-structure provider | Structure service | Delivered | Namespace, type, member, block, region, and comment fold ranges |
| Document outline and breadcrumbs | Code-structure provider | Structure/navigation services | Delivered | Accessible outline flyout and clickable live-buffer breadcrumbs |
| Workspace symbol search | Monaco/OmniSharp navigation | Navigation/types services | Delivered | Debounced bounded Roslyn search dialog with cancellation |
| Model semantic search, calls, hierarchy, tests | No equivalent complete typed agent surface | Underlying Roslyn services | Task 047 | Bounded paged typed toolsets and shared source identity |
| Parameter and type inlay hints | Inlay-hint provider and settings | Inlay-hint service and cache | Delivered | Typed SQLite-backed settings, exact visible-buffer Roslyn production, inline renderer, stale rejection, and tests |
| CodeLens | No complete equivalent found | No complete equivalent found | Delivered for available actions | Visible declarations expose bounded reference, implementation, and associated-test actions; queries resolve only on selection. Run/Debug stay hidden because no typed per-declaration execution target exists yet |
| Document/range/on-type formatting | Range and on-type providers | Formatting services | Delivered | Document, selection, Roslyn syntax-changed spans, paste ranges, and supported `;`, `}`, and new-line triggers use closed typed previews. Settings control automatic paths. Editor changes are one undoable unsaved replacement; model apply requires exact fingerprint, file grant, atomic write, and post-check |
| Organize/fix usings | Code actions | Refactoring services | Delivered for closed import operations | Sorting/grouping and compiler-proven unused-import removal share the fingerprinted transformation path. Missing-type discovery returns only exact namespaces that Roslyn recompiles and proves bind at the caret; the editor offers accessible choices and models must discover before preview/apply |
| Quick fixes, refactorings, fix-all | Code-action provider | V1/V2 refactoring services | Delivered for the closed single-document catalog | `Ctrl+.` and Quick fix discover preflighted compiler/style fixes and local or exact-selection refactorings from pinned Roslyn providers. The menu labels document-wide fixes. Every choice carries an opaque ID and scope through preview/fingerprint/apply; custom host operations, project changes, added files, and cross-document actions are omitted. Models and inbound MCP share the read result; no raw action executor exists |
| Generated and metadata/decompiled source | Limited source navigation | Decompilation service | Partial | Generated output and public/protected metadata signatures open through exact-buffer opaque handles as labeled read-only virtual documents. They are bounded and excluded from repository and layout persistence. Full method-body decompilation still requires a reviewed public dependency |
| Syntax tree, symbol, generated, and IL views | Syntax-tree and IL services/tools | Compiler services | Delivered | The editor, role tools, and opt-in inbound MCP share closed read-only exact-buffer views. Results name project version, TFM, configuration, assembly, document version, and compilation identity; syntax and generated output are bounded and IL is emitted locally from the exact compilation |
| Configurable keybindings | Monaco bindings | Not an editor concern | Delivered | One typed command/gesture snapshot drives runtime dispatch, Settings, header hints, and command discovery. Whole-set validation blocks conflicts and reserved shortcuts; reset and strict bounded `harness-keybindings-v1` import/export are implemented |
| Vim mode | Monaco Vim option | Not an editor concern | Delivered | Persistent Standard/Vim choice; visible Normal, Insert, Visual, and Visual Line state; counted core motions/operators; clipboard register; preedit suspension; platform-shortcut pass-through |
| Project User Secrets | No equivalent complete project workflow found | No equivalent complete workflow found | Planned | Standard store; separate redacted actions; capture interlock |
| Script playground, rich dump, spreadsheet export, web shell | Delivered NetPad product features | Not applicable | Excluded | Harness.NET is a repository IDE and agent workbench, not a script notebook |
| Out-of-process OmniSharp server | NetPad downloads and manages OmniSharp | The reference server itself | Conditional | Requires measured benefit and an ADR 012 amendment; currently prohibited |

## Reference locations

The NetPad comparison is grounded in
`src/Plugins/NetPad.Plugins.OmniSharp/Features`,
`src/Core/NetPad.Runtime/CodeAnalysis`, and its Monaco editor providers and Vim
configuration. The OmniSharp comparison is grounded in
`src/OmniSharp.Roslyn.CSharp/Services`. Recheck these locations only when either
pinned revision changes, and update this matrix in the same change.
