# Metadata decompilation acceptance

This record closes Task 049's remaining metadata-navigation gap. F12, role tools, and
opt-in inbound MCP now resolve the same exact-buffer virtual document. Generated
source is unchanged. A metadata result contains locally reconstructed C# method bodies
when a matching implementation assembly exists and an explicit signature fallback
when it does not.

## Dependency review

- Package: `ICSharpCode.Decompiler` `10.1.1.8388`, pinned centrally and referenced
  only by Data Access.
- Upstream: ILSpy stable `v10.1.1`, commit
  `1377eb6e7351b21112858c8c1df39848f40181ec`, branch
  `refs/heads/release/10.1`.
- License: MIT. Harness.NET copies no ILSpy source. The package is attributed in the
  generated virtual-document header and this record.
- Integrity: official NuGet archive SHA-512
  `1512c9e2748b6745616110fbbc96726876264f39307b1d19405090abe03bbfb87f4aa5ba0490112160ef8ab6fbe996eb17e6cf02c0bda1311115f0cf2e1bea37`;
  restored NuGet hash
  `Gel09L54mMdYMBivl6DSw8OtQR29joHvmqEPdsA+/lJE0Q8qgwCOyV7NkMIZC0BFCjluDNzqEQzUnRx/OtvB1Q==`.
- Supply chain: the archive includes its NuGet repository signature and an SPDX 2.2
  manifest. Its declared dependencies are `System.Collections.Immutable >= 9.0.0`
  and `System.Reflection.Metadata >= 9.0.0`; both were already supplied by the .NET 10
  graph. The SBOM adds one direct component and no new transitive component.
- Sources: the [official package](https://www.nuget.org/packages/ICSharpCode.Decompiler/10.1.1.8388)
  and [upstream release](https://github.com/icsharpcode/ILSpy/releases/tag/v10.1.1).

ADR 012 records adoption, authority, fallback, upgrades, and rollback.

## Boundaries

- Roslyn chooses the symbol and compilation. The adapter accepts no arbitrary path.
- Candidate files come only from exact compilation references and the running .NET
  trusted-platform assembly set. The managed assembly identity must match exactly.
- Reference assemblies are not presented as implementations. Harness.NET first uses
  the exact matching local runtime assembly; otherwise it returns the existing
  public/protected signature view with `decompilation_unavailable`.
- One selected member is decompiled. A type result is allowed only when its local image
  contains an implementation body. At most 16 matching implementation candidates,
  2,048 exact local resolver entries, 256 MiB per image, and 2 MiB of returned text are
  accepted.
- The operation performs no download, Restore, process start, reflection load,
  project execution, write, or persistence. It is cancellable and session/buffer/hash
  stale-safe.
- Output is labeled as a read-only reconstruction, carries project, target,
  configuration, assembly, and compilation identity, and is excluded from layouts and
  user repositories.

## Verification

Deterministic tests prove:

- a .NET runtime metadata definition resolves to labeled decompiled source;
- a locally emitted referenced assembly reconstructs the selected method body;
- a reference-only assembly falls back to an explicit metadata signature;
- buffer-version changes invalidate the opaque handle;
- the new kind crosses the Business Logic boundary;
- F12 opens an accessible read-only editor and layout persistence excludes its text;
- role and inbound MCP definition paths continue to eagerly resolve virtual text before
  the short-lived Roslyn session closes.

The editor verifier, complete solution build/test, format gate, Linux x64 publish, and
secret scan remain the final release gates for this slice.
