# Local typed-tool workflow liveness

Task 070 is complete.

## Dogfood findings

Three bounded Tic-Tac-Toe runs exercised the production Harness workflow against the
local Ollama server. Qwen 3.8 filled the Lead and Reviewer roles, while GPT-OSS 20B was
initially used as Implementer. The runs produced real typed file mutations and exposed
five orchestration failures rather than a context-retrieval deficit:

- a successful tool mutation followed by empty model text did not create a durable
  implementation checkpoint;
- a dotted directory grant was optimistically treated as an exact file;
- a Reviewer could return a revise decision without inspecting the diff or evidence;
- completed delegated tasks did not have a workflow-owned final Build/Test gate; and
- the live regression driver continued polling after the inbound operation failed.

The second run proved that workflow-owned Build/Test evidence was recorded and that
invalid generated tests were rejected, but its long validation transcript exceeded a
checkpoint summary boundary. A third run confirmed that disabling Implementer
reasoning improved action latency. During that run, the 13 GB GPT-OSS allocation lost
the 16 GiB Radeon Vulkan device. Ollama recovered only to CPU because the server is an
LXC container and its PCI reset control is read-only inside the container.

Host inspection later identified the failure as an AMD PSP runtime-resume timeout,
not a changed LXC passthrough configuration. The run alternated Qwen 3.8 27B and
GPT-OSS 20B at an adapter-imposed 32,768-token context. Ollama repeatedly predicted
13.9–19.0 GiB allocations, evicted one roughly 12–14 GiB model for the other, and
reached about 137 MiB free system memory. The PSP resume failed during the final swap,
after which Ollama discovered only the CPU Vulkan fallback.

A controlled host reboot restored the device. The dedicated headless GPU now has AMD
runtime power management disabled, and Ollama is bounded to one loaded model, one
parallel request, an eight-request queue, an 8,192-token server default, and 1 GiB of
reserved GPU headroom. A real 3.2B smoke fully offloaded to VRAM at an 8,192-token
context and generated about 124 tokens/second. Host and container DRM opens remained
healthy afterward. A subsequent Harness-oriented Ornith 9B smoke also fully offloaded
at 8,192 tokens and emitted the exact requested typed `read_range` call at about 22.5
generated tokens/second, without new kernel errors. Harness also replaces its hidden
32,768-token minimum with a typed per-Ollama maximum-agent-context setting: the shipped
8,192-token limit is visible, validated, persisted through Settings, and combined with
request size and the model's advertised context.

The server remains on Ollama 0.32.3 with flash attention and quantized KV cache. The
specialized `harness-qwen2.5-coder:14b-v1` profile is the next Implementer candidate:
it occupies materially less VRAM than GPT-OSS and has an action-first typed-tool
system profile. The recovered GPU is ready for the next bounded comparison, but that
profile is not promoted until a representative workflow passes. No model blob,
machine path, or conversation content is stored in the repository.

No LSP, MCP server, document store, relational database, or vector database was added.
The observed faults were state-transition, evidence, and validation-boundary defects;
an additional retrieval service would have increased maintenance and context pressure
without addressing them.

## Delivered behavior

- The role runner turns an empty Implementer response into a deterministic durable-
  evidence handoff only after tools ran; other empty responses fail explicitly.
- A missing exact-file bootstrap falls back to the normal bounded typed-tool path, so
  dotted directory grants do not dead-end.
- A text-only Reviewer gets one correction turn that requires `inspect_git` and
  `list_tool_evidence` before returning the bounded review decision.
- The workflow runs Build then Test after all delegated tasks and after each review
  correction, so every changed implementation state is validated before review. A
  failure gets one Implementer repair against the most frequently cited authorized C#
  path, then one revalidation. Persistent failure becomes concise, retryable direction.
- Review corrections accept either new mutation or verification evidence. Completed-
  task correction states retain an Implementer retry route.
- Failed and cancelled inbound operations terminate the live driver immediately with
  the persisted failure instead of timing out against stale goal state.

## Verification

The focused Business Logic liveness selection passes all 43 tests, the complete
architecture project passes all 4 tests, and the Python regression driver passes all
12 tests. Repository metadata validation passes and a complete solution build reports
zero warnings and errors.

All 848 deterministic .NET tests pass: 16 analyzer, 4 architecture, 314 Business
Logic, 291 Data Access, 22 Host, 177 Avalonia Presentation, 22 terminal Presentation,
and 2 Avalonia UI tests. The first parallel solution invocation exhausted the 3.7 GiB
`/tmp` tmpfs because a retained 2.8 GiB dogfood directory was still present; after
removing that disposable directory, each long-running test assembly passed with ample
space. Avalonia was rerun without an overlapping detached test host to avoid a
test-environment `HeadlessUnitTestSession.Dispose` race. `git diff --check` is clean.
