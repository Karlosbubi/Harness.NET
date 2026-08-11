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

Settings → MCP connections can add, edit, enable, disable, remove, refresh, and show
protocol, eligible/rejected counts, and failures. Changes require restart because
active clients and schemas are fixed for the process lifetime.

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
