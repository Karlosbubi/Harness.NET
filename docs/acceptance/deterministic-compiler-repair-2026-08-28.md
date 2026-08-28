# Deterministic compiler repair before model retry

Task 072 is complete. This slice removes one avoidable local-model round trip without
turning Roslyn into a generic executor or guessing at developer intent.

## Delivered behavior

- A model-authored C# candidate that introduces only `CS0246` or `CS0103` compiler
  errors receives an in-memory repair attempt before rejection.
- At most four diagnostics are considered. Each requires exactly one namespace that
  Roslyn proves binds the unresolved type or name at the diagnostic position.
- The existing closed `AddMissingImport` preview produces the AST edit. The stage
  accepts only one edit of the same document against the same exact baseline.
- Every intermediate candidate is revalidated. Disk mutation occurs only when the
  final candidate introduces no compiler warning or error, using the existing atomic
  write and persisted-result validation path.
- The `FileEditView` result records the repair kind, compiler diagnostic, namespace,
  and bound symbol. Prompts, hidden reasoning, and provider payloads are not added.
- Ambiguous namespaces, stale previews, unsupported diagnostics, Roslyn issues,
  multi-file output, more than four blockers, and remaining errors or warnings keep
  the original fail-closed result and write nothing.

The stage does not attempt nullable-flow repair, syntax invention, test repair,
behavioral changes, or general code-action selection. Those still require an
Implementer or developer decision.

## Constrained-host measurement

The comparison used local inference only and retained no prompt or response content.
On the current eight-thread Ryzen host with GPU execution unavailable:

| Path | Model size | Result |
|---|---:|---|
| Deterministic semantic-edit regression decision | none | completed in 0.08 seconds |
| One requested tool call | Qwen2.5-Coder 14.8B Q4 | timed out at 55.07 seconds; model load took 43.43 seconds |
| One requested tool call | Llama 3.2 3.2B Q4 | timed out at 55.07 seconds; model load took 21.04 seconds |

The model calls were deliberately tiny and used a 48-token output ceiling. Neither
returned a tool call before timeout. This does not compare semantic coding quality; it
establishes that a compiler operation is the appropriate latency and reliability path
for this closed decision on the constrained host.

GPU inspection found the RX 9060 XT, `amdgpu`, both DRM nodes, and 16 GiB VRAM, but a
plain open of either DRM node returns `EINVAL`; Vulkan consequently exposes only
`llvmpipe`. The container also has no `/dev/kfd`. Repair requires host-level device
reset/rebinding or corrected LXC device configuration and remains separate from this
repository slice.

## Verification

- 5 focused pipeline tests cover unique repair, ambiguity, remaining diagnostics,
  the four-diagnostic bound, stale preview, atomic persistence, and typed evidence.
- 30 existing workspace mutation tests continue to cover ordinary valid and rejected
  edits, exact baselines, post-apply validation, evidence, and interruption.
- 2 real Roslyn adapter tests prove missing-import discovery and the closed
  `AddMissingImport` transformation rather than substituting fake compiler behavior.
- All 318 Business Logic tests pass.
- All 850 deterministic non-live tests across the solution pass.
- The solution builds with zero warnings and errors, the source-size guard passes,
  repository metadata verifies, and `git diff --check` is clean.
