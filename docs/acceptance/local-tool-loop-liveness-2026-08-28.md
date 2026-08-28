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
LXC container and its PCI reset control is read-only inside the container. Host-level
GPU reset or container restart from the virtualization host is required before the
next representative live comparison.

The server remains on Ollama 0.32.3 with flash attention and quantized KV cache. The
specialized `harness-qwen2.5-coder:14b-v1` profile is the next Implementer candidate:
it occupies materially less VRAM than GPT-OSS and has an action-first typed-tool
system profile. A CPU smoke completed, but CPU fallback is not representative enough
to promote the route. No model blob, machine path, or conversation content is stored
in the repository.

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

All 843 deterministic .NET tests pass: 16 analyzer, 4 architecture, 313 Business
Logic, 295 Data Access, 21 Host, 170 Avalonia Presentation, 22 terminal Presentation,
and 2 Avalonia UI tests. The first parallel solution invocation exhausted the 3.7 GiB
`/tmp` tmpfs because a retained 2.8 GiB dogfood directory was still present; after
removing that disposable directory, each long-running test assembly passed with ample
space. Avalonia was rerun without an overlapping detached test host to avoid a
test-environment `HeadlessUnitTestSession.Dispose` race. `git diff --check` is clean.
