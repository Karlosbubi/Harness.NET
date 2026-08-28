# Bounded per-role reasoning policy

Task 069 is complete.

## Dogfood finding

The primary local Ollama server is version 0.32.3 with a 16 GiB Radeon RX 9060 XT.
One-call typed reasoning-and-tool smokes passed with `ornith:9b` in 16 seconds and
`qwen3.8:27b-q3` in 33 seconds.

The larger Tic-Tac-Toe workflow then exposed the difference between adapter
compatibility and usable orchestration. With Qwen 3.8 as Lead and Reviewer and Ornith
as Implementer, the first Lead operation ran from 13:54:31 to 14:08:03 UTC before
proposing a valid plan. Its final inspection cycle generated 3,151 tokens before
requesting the next typed tool. Ornith subsequently exhausted the bounded agent loop
without producing a usable edit and entered explicit recovery.

Disabling reasoning shortened Qwen planning to about three minutes, but three
Implementer comparisons (`ornith:9b`, a Harness-oriented Ornith profile, and a
Harness-oriented Qwen 2.5 Coder profile) then exhausted their bounded retries without
making a mutation. A later `gpt-oss:20b` Lead comparison returned no structured plan
while reasoning was disabled. The product therefore preserves provider behavior by
default and treats `Disabled` as an explicit, measured quality/latency tradeoff.

A final role-specialized comparison kept Qwen 3.8 as Lead and Reviewer with reasoning
disabled while assigning provider-default reasoning to `gpt-oss:20b` as Implementer.
The Lead produced an accepted four-slice plan in 2 minutes 3 seconds. The Implementer
then emitted a native `apply_file_edit` request on its first task, and the typed
mutation plus both candidate and applied Roslyn validation succeeded. Its generation
rate was about 57 tokens/second, compared with about 9 tokens/second for the dense
Qwen route. This is materially better action throughput than the other tested
Implementers, but it is not yet a production route recommendation: after the edit,
two follow-up model responses completed and the workflow remained `Running` without
persisting a new checkpoint or invoking the required typed Build/Test operations.
That post-tool liveness failure is a separate orchestration defect for the next slice.

The failed comparisons are machine-local reproduction data under
`artifacts/local-model-regression/`; they are ignored and are not durable evidence in
another clone. No remote or paid provider was configured.

The server can run the 20-billion-parameter comparison fully on its 16 GiB GPU, but
the container exposes Vulkan graphics without the ROCm `/dev/kfd` compute device.
Actual adapter training is therefore deferred until host GPU compute passthrough is
available. Prompt-profile aliases were tested without replacing their base models;
neither profiles nor model blobs are repository content.

## Delivered behavior

- `AgentReasoningPolicy` is a Business Logic role policy with `Disabled` and
  `ProviderDefault` values.
- Fresh local and remote defaults preserve `ProviderDefault`; Settings can persist
  `Disabled` independently for each role.
- SQLite schema 31 persists the policy. Existing saved routes migrate to
  `ProviderDefault`, preserving pre-upgrade behavior.
- Goal resolution carries the effective role policy into the provider-neutral chat
  request. Structured local file proposals still force reasoning off.
- Settings → Models & roles exposes the choice, saves it with the model route, and
  displays the effective policy.

Provider-specific low, medium, and high settings are deliberately not presented as
portable role choices because Ollama model support differs. Users can instead reserve
provider-default thinking for selected deep roles and disable it for responsive tool
loops.

## Verification

The focused routing, persistence, migration, provider mapping, and Settings tests
pass. The repository build has zero warnings and errors. The complete deterministic
suite passes all 832 tests: 16 analyzer, 4 architecture, 302 Business Logic, 295 Data
Access, 21 Host, 170 Avalonia Presentation, 22 terminal Presentation, and 2 Avalonia
UI tests. The deterministic regression fixture accepts the new repeated role flags,
the repository metadata verifier passes, `git diff --check` is clean, and the
production Avalonia AT-SPI verifier passes against the changed Settings surface.
