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
| Document/range/on-type formatting | Range and on-type providers | Formatting services | Partial | Document and selection formatting use closed typed Roslyn previews; editor commands update one undoable live-buffer change, while model apply requires exact fingerprint, file grant, atomic write, and post-check. Changed-span, paste, and on-type formatting remain |
| Organize/fix usings | Code actions | Refactoring services | Delivered for closed import operations | Sorting/grouping and compiler-proven unused-import removal share the fingerprinted transformation path. Missing-type discovery returns only exact namespaces that Roslyn recompiles and proves bind at the caret; the editor offers accessible choices and models must discover before preview/apply |
| Quick fixes, refactorings, fix-all | Code-action provider | V1/V2 refactoring services | Partial | Missing-type imports are the first contextual quick fix and unused imports provide a document-bounded fix-all. The broader closed operation catalog remains; never expose a raw action executor |
| Generated and metadata/decompiled source | Limited source navigation | Decompilation service | Partial | Destination kinds exist; bounded labeled read-only virtual documents do not |
| Syntax tree, symbol, generated, and IL views | Syntax-tree and IL services/tools | Compiler services | Planned | Exact project/TFM/configuration/document/compilation identities required |
| Configurable keybindings | Monaco bindings | Not an editor concern | Planned | Validation, conflicts, reset, safe import/export, command discovery |
| Vim mode | Monaco Vim option | Not an editor concern | Planned | Optional mode without breaking IME, AT-SPI, or platform shortcuts |
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
