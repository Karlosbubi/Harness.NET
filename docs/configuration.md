# Configuration

Harness.NET loads configuration once at startup in this order:

1. `harness.xml` beside the executable.
2. `$XDG_CONFIG_HOME/harness.net/harness.xml`, or
   `~/.config/harness.net/harness.xml` when `XDG_CONFIG_HOME` is unset.
3. Environment variables prefixed with `HARNESS_`.
4. Command-line configuration keys.

Later sources override earlier sources. An XDG file may contain only changed values.

## Provider modules

Each child of `Providers` is a named Ollama or OpenRouter module. Routes refer to the
module name.

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Harness>
  <Providers>
    <Ollama>
      <Kind>Ollama</Kind>
      <Endpoint>http://192.168.1.101:11434</Endpoint>
      <ChatModel>gemma4:latest</ChatModel>
      <EmbeddingModel>embeddinggemma</EmbeddingModel>
      <EmbeddingDimensions>768</EmbeddingDimensions>
      <ConnectTimeoutSeconds>5</ConnectTimeoutSeconds>
      <RequestTimeoutSeconds>600</RequestTimeoutSeconds>
    </Ollama>
  </Providers>
  <Routing>
    <MainLlm>Ollama</MainLlm>
    <Reviewer>Ollama</Reviewer>
    <ToolLlm>Ollama</ToolLlm>
    <Embedding>Ollama</Embedding>
  </Routing>
</Harness>
```

All routes are validated at startup. `Routing:Embedding` is independent of the chat
routes. Provider, model, embedding dimensions, and chunking version form the vector
partition identity; incompatible partitions are never mixed.

Use Settings → Model providers to edit endpoint, chat/embedding defaults, embedding
dimensions, secret references, and timeouts. Settings writes the private XDG override
without replacing unrelated XML. Active providers, routes, clients, and index
identity do not change during the process, so these edits require restart.

Temporary environment override:

```bash
HARNESS_Providers__Ollama__Endpoint=http://localhost:11434 \
dotnet run --project src/Harness.Host/Harness.Host.csproj
```

Environment key separators use `__`.

### OpenRouter

```xml
<OpenRouter>
  <Kind>OpenRouter</Kind>
  <Endpoint>https://openrouter.ai</Endpoint>
  <ChatModel>openai/gpt-5-mini</ChatModel>
  <EmbeddingModel>openai/text-embedding-3-small</EmbeddingModel>
  <EmbeddingDimensions>1536</EmbeddingDimensions>
  <ApiKeySecret>openrouter-api-key</ApiKeySecret>
  <ApiKeyEnvironmentVariable>OPENROUTER_API_KEY</ApiKeyEnvironmentVariable>
  <ConnectTimeoutSeconds>10</ConnectTimeoutSeconds>
  <RequestTimeoutSeconds>600</RequestTimeoutSeconds>
</OpenRouter>
```

The API key is not stored in XML. Harness.NET resolves `ApiKeySecret` through Linux
Secret Service, then uses the configured environment variable as fallback. It does
not load repository `.env` files. Environment names are case-sensitive on Linux.

Settings accepts a write-only API key and sends it directly to Secret Service. The
control is cleared after save. Snapshots report only `Missing`, `Configured`, or
`Unavailable`.

A key or route does not authorize a paid call. The goal spend mode must be Unlimited
or Capped. Every request requires pricing and cost reservation. Capped calls may
derive a provider output boundary from remaining money; Unlimited calls omit an
application token ceiling. Strict workspace privacy requests no-collection and
zero-data-retention routing.

Startup catalog discovery performs no inference. Model choices persist separately for
Lead, Implementer, and Reviewer. Business Logic filters each role to models that
declare its required capabilities. Missing pricing is shown and paid calls fail
closed.

## MCP connections

Each child of `McpConnections` is a named Streamable HTTP endpoint:

```xml
<McpConnections>
  <AvaloniaDocs>
    <Endpoint>https://docs.example.test/mcp</Endpoint>
    <RequestTimeoutSeconds>30</RequestTimeoutSeconds>
    <Enabled>true</Enabled>
  </AvaloniaDocs>
</McpConnections>
```

Rules:

- remote endpoints require HTTPS; plain HTTP is allowed only for loopback;
- discovery uses official C# SDK 2.x and starts with stateless protocol `2026-07-28`;
- no MCP session ID is persisted;
- stdio and legacy SSE transports are disabled;
- only enabled tools that declare read-only and non-destructive behavior reach agents;
- ambiguous or unsafe tools remain visible in Settings and are rejected;
- at most 256 tools may be advertised and 32 eligible tools exposed per connection;
- descriptions and schemas also have size limits;
- there is no generic call-by-name MCP function.

The default `ReadOnly` access mode keeps those rules unchanged. A controller instance
may use a separate local Harness.NET worker by selecting `HarnessControl` in Settings.
Its persisted shape is:

```xml
<worker>
  <Endpoint>http://127.0.0.1:57431/mcp</Endpoint>
  <RequestTimeoutSeconds>60</RequestTimeoutSeconds>
  <Enabled>true</Enabled>
  <Access>HarnessControl</Access>
  <ClientId>controller</ClientId>
  <BearerTokenReference>harness-mcp-connection-worker-bearer</BearerTokenReference>
  <AllowedTools>harness_application
harness_create_goal
harness_goals
harness_workflow_evidence
harness_start_planning</AllowedTools>
</worker>
```

The bearer value is never stored in XML. Paste the worker token in Settings; it is
written to Secret Service under the reference. Control requires a loopback endpoint,
an initialized server whose name is exactly `Harness.NET`, a valid client ID, and
1–32 distinct exact `harness_` tool IDs. Only Lead receives those tools. The worker
still applies every normal instance, workspace, goal, plan, spending, worktree, and
approval check. Configure a directed controller→worker topology; automatic cycle
detection and arbitrary mutual/self-control are not implemented.

Settings → MCP connections can add, edit, enable, disable, remove, refresh, and show
access mode, credential presence, exact allowlist, protocol, eligible/rejected counts,
and failures. Changes require restart because active clients and schemas are fixed for
the process lifetime.

## Inbound Harness control

Settings → Harness control writes the private `InboundMcp` section and applies it to
the running process:

```xml
<InboundMcp>
  <Enabled>false</Enabled>
  <Mode>Normal</Mode>
  <Endpoint>http://127.0.0.1:57431/mcp</Endpoint>
  <RequestTimeoutSeconds>30</RequestTimeoutSeconds>
  <ResultLimit>500</ResultLimit>
  <AuditRetention>1000</AuditRetention>
  <AllowedClients>
    <Client>codex</Client>
  </AllowedClients>
  <AllowedTools>
    <Tool>harness_application</Tool>
    <Tool>harness_workspace</Tool>
  </AllowedTools>
  <ApprovalRequiredTools>
    <Tool>harness_build</Tool>
  </ApprovalRequiredTools>
</InboundMcp>
```

Only `http`, loopback hosts, ports 1024–65535, known closed tool IDs, client IDs of
1–128 characters, timeouts of 1–300 seconds, result limits of 1–5000, and audit
retention of 0–100000 are accepted. An approval-held tool is omitted from discovery.

For automated isolated evaluation only, start the host with both
`--mcp-evaluation-root /tmp/<dedicated-directory>` and
`--mcp-evaluation-token-file /tmp/<dedicated-directory>/mcp.token`. The token file
must be a regular owner-only file directly inside the evaluation root and contain one
48-byte Base64 token. Harness loads it into the volatile secret store and deletes the
file before starting the listener. The token is not placed in process arguments,
logs, normal configuration, SQLite, or Secret Service. Normal mode rejects this
bootstrap option.
The bearer token is never stored in XML. Normal mode uses Secret Service;
IsolatedEvaluation uses process-local volatile storage.

Mutating calls require the current `instanceId` returned by `harness_application`.
Stale process identities fail before dispatch. Results include the applicable
workspace, source, goal, document/baseline, freshness, truncation, and continuation
identity rather than relying on endpoint continuity.

Start a disposable evaluation instance with a dedicated child of the system temporary
directory:

```bash
dotnet run --project src/Harness.Host/Harness.Host.csproj -- \
  --mcp-evaluation-root /tmp/harness-evaluation-1
```

Then choose IsolatedEvaluation and enable the server in Settings. That instance has a
separate database/configuration/cache/state root, no persisted provider credentials,
and one resettable deterministic fixture. It may use deterministic fakes or an
explicitly configured Ollama provider. It cannot expose a normal repository.

## Documentation, dependency, and SBOM research

Settings → Documentation & dependencies writes a `Research` section to the private
XDG override without replacing unrelated configuration:

```xml
<Research>
  <ExactLocalEnabled>true</ExactLocalEnabled>
  <LocalIndexEnabled>true</LocalIndexEnabled>
  <McpEnabled>true</McpEnabled>
  <WebEnabled>true</WebEnabled>
  <Offline>false</Offline>
  <RefreshPolicy>OnDemand</RefreshPolicy>
  <MaximumResults>5</MaximumResults>
  <MaximumCharacters>12000</MaximumCharacters>
  <MaximumCacheAgeHours>168</MaximumCacheAgeHours>
  <RetentionDays>30</RetentionDays>
  <IndexRoots>
    <Root>/srv/docs/Avalonia/12.1.0</Root>
  </IndexRoots>
  <McpTools>
    <Tool Connection="AvaloniaDocs" Name="search_docs" />
  </McpTools>
  <WebEndpoints>
    <Endpoint>https://learn.microsoft.com/api/search</Endpoint>
  </WebEndpoints>
  <PackageSources>
    <Source>https://api.nuget.org/v3/index.json</Source>
  </PackageSources>
</Research>
```

Remote endpoints require HTTPS; loopback HTTP is allowed for development. MCP routes
refer to a connection and exact discovered tool name. The tool must remain closed,
read-only, non-destructive, and agent-eligible. Web requests contain only library,
version, question, locale, and result limit. Package sources must expose NuGet v3
registration and package-content resources.

Documentation cache entries live under `$XDG_CACHE_HOME/harness.net/documentation`.
Their identity includes source, library, version, question, adapter schema, and privacy
mode. Retention can delete them; they are not application state or repository files.
Offline mode permits exact local, indexed, and cached results only.

Dependency inspection reads project XML, `Directory.Packages.props`,
`packages.lock.json`, and existing `obj/project.assets.json`. It never invokes Restore
or MSBuild targets. Candidate validation downloads bounded registration and package
metadata from configured sources and checks exact availability, framework/runtime
assets, dependencies, listing/deprecation, advisories, license, repository provenance,
and SHA-512. Missing metadata remains unknown.

## Visual verification

Settings → Visual verification persists ordinary capture defaults in SQLite:

- capture enabled;
- maximum encoded frame size from 1 through 16 MiB;
- retention from 1 through 90 days;
- maximum retained frames per goal from 1 through 100;
- remote-model access, disabled by default.

The page also reports live XDG Screenshot portal availability. Frames and manifests
are private XDG state, not XML configuration, backup content, or repository files.
Changing these settings takes effect immediately and cleanup applies the current
retention policy.

## Framework rules

```xml
<Framework>
  <Rules>
    <ApprovalPolicy>
      <Value>explicit</Value>
      <Precedence>0</Precedence>
      <Layer>global</Layer>
      <Locked>true</Locked>
    </ApprovalPolicy>
  </Rules>
</Framework>
```

Higher precedence is more specific. A locked effective rule blocks later overrides.
Conflicting values at the same precedence invalidate the effective framework until
resolved.

## Operational modes

- `--ui=avalonia`: select the default desktop UI.
- `--ui=terminal`: select Terminal.Gui; attached input and output are required.
- `--no-ui`: initialize, migrate, print readiness/schema, and exit.
- `--wait-for-shutdown`: initialize, report readiness, and wait for SIGINT/SIGTERM.

`--ui` cannot be combined with backup, wait, or non-UI modes.

## User themes

User palettes are loaded from `$XDG_CONFIG_HOME/harness.net/themes/*.xml`. The
selected theme ID is stored in application state.

```xml
<harnessTheme version="1" id="nord" name="Nord" base="dark">
  <color token="Window" value="#2E3440" />
  <color token="TextPrimary" value="#ECEFF4" />
  <color token="Accent" value="#88C0D0" />
</harnessTheme>
```

IDs allow lowercase letters, digits, dots, underscores, and hyphens. Colors are
opaque `#RRGGBB`. Supported names come from `ThemeColorToken`, including the
`Code*` syntax tokens.

Harness.NET reads at most 64 theme files of 64 KiB each. It rejects malformed files,
unsafe contrast, DTDs, and external resources. Themes cannot load AXAML, code, fonts,
includes, or external assets. Use Reload after editing a palette.
