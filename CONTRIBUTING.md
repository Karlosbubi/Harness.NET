# Contributing

Start with the repository [working agreement](AGENTS.md). It is the single source of
truth for architecture, semantic types, verification, fiscal safety, branches,
commits, pushes, and draft pull requests. Human and agent contributions follow the
same agreement.

## Before changing code

1. Read the [documentation map](docs/README.md), `README.md`, and
   [framework rules](docs/framework.md).
2. Read the accepted decisions relevant to the feature and record a proposed
   exception before implementing one.
3. Create a dedicated branch from the current default branch. Do not develop or push
   directly on `main`.

## Delivery

Keep one end-to-end feature slice per branch. Synchronize documentation with behavior,
run the narrowest relevant tests and at least a warnings-as-errors build, then commit,
push, and open a draft pull request. Include commands and results as review evidence.
The [verification catalog](eng/README.md) maps specialized gates and prerequisites.

Routine local verification:

```bash
python3 eng/verify-repository-metadata.py
dotnet restore Harness.slnx
dotnet build Harness.slnx --no-restore
dotnet test Harness.slnx --no-build --no-restore --filter Tier=Fast --maxcpucount:1
dotnet test Harness.slnx --no-build --no-restore --filter Tier!=Live --maxcpucount:1
```

Live and paid-provider checks are never implied by a configured key. They require
explicit approval for the smallest practical bounded call, as described in the
working agreement and the relevant acceptance record.

## Evidence

Durable evidence is deliberately committed under `docs/acceptance/` only after
redaction and size review. Output below ignored `artifacts/` is machine-local
reproduction data, not repository evidence. See
[ADR 027](docs/decisions/027-contributor-verification-and-dependency-governance.md).
