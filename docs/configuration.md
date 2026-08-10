# Configuration

Harness.NET loads typed configuration at startup through
`Microsoft.Extensions.Configuration` in this order:

1. The shipped `harness.xml` beside the executable.
2. An optional XDG override at `$XDG_CONFIG_HOME/harness.net/harness.xml`, or
   `~/.config/harness.net/harness.xml` when `XDG_CONFIG_HOME` is unset.
3. Environment variables prefixed with `HARNESS_`.
4. Command-line configuration keys.

Later sources override earlier sources. The shipped file is a complete working
example. A user override may contain only the values it changes:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<Harness>
  <Providers>
    <LocalCoding>
      <Kind>Ollama</Kind>
      <Endpoint>http://192.168.1.101:11434</Endpoint>
      <ChatModel>gemma4:latest</ChatModel>
      <EmbeddingModel>embeddinggemma</EmbeddingModel>
      <EmbeddingDimensions>768</EmbeddingDimensions>
      <ConnectTimeoutSeconds>5</ConnectTimeoutSeconds>
      <RequestTimeoutSeconds>600</RequestTimeoutSeconds>
    </LocalCoding>
  </Providers>
  <Routing>
    <MainLlm>LocalCoding</MainLlm>
    <Reviewer>LocalCoding</Reviewer>
    <ToolLlm>LocalCoding</ToolLlm>
    <Embedding>LocalCoding</Embedding>
  </Routing>
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
</Harness>
```

## Stateless MCP connections

Each child of `McpConnections` is a named remote Streamable HTTP endpoint:

```xml
<McpConnections>
  <AvaloniaDocs>
    <Endpoint>https://docs.example.test/mcp</Endpoint>
    <RequestTimeoutSeconds>30</RequestTimeoutSeconds>
    <Enabled>true</Enabled>
  </AvaloniaDocs>
</McpConnections>
```

Harness.NET uses the stable official C# MCP SDK 2.x. It does not force a legacy
protocol version, so discovery starts with the stateless `2026-07-28`
`server/discover` flow. Streamable HTTP is explicit; no MCP session identifier is
persisted or resumed, and stdio/SSE transports are not enabled. Remote endpoints must
use HTTPS. Plain HTTP is accepted only for loopback development endpoints.

Enabled connections are discovered at application startup without model inference.
Only tools that explicitly declare `readOnlyHint: true` and do not declare
`destructiveHint: true` are namespaced and exposed to Lead, Implementer, and Reviewer.
Missing or unsafe annotations are counted in Settings but fail closed. Enabling an
endpoint explicitly trusts it to execute those read-only calls; mutating MCP tools
require a future typed approval and are not reachable through a generic call-by-name
escape hatch. Catalogs are bounded to 256 advertised tools, with at most 32 eligible
tools per connection exposed to agents. Descriptions and individual input/output
schemas are also bounded before they may enter model context; duplicates and excess
tools remain rejected and visible rather than being silently truncated into authority.

Use **Settings → MCP connections** to add, edit, enable/disable, remove, and inspect
connections. It shows negotiated protocol, eligible/rejected tool counts, and
connection failures. Changes are written to the private XDG override while preserving
unrelated configuration and require restart because active MCP clients and tool
schemas are fixed for the process lifetime.

Each child of `Providers` is a named module. Routing refers to module names, not
implementation types, so several differently configured modules can use the same
provider implementation. All routes are validated at startup. Supported kinds are
`Ollama` and `OpenRouter`.

OpenRouter modules add semantic secret references while keeping the credential out
of XML:

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

Every OpenRouter inference request also requires an authorized goal with a persisted
remote-spend mode. New goals default to Unlimited; Settings and goal creation expose
Capped and Local-only as prominent cost-control opt-ins. Explicit goal-role selection authorizes planning inference before plan
approval without granting repository mutation; plan approval separately authorizes
the isolated worktree capabilities. For capped goals, the connector derives a provider
request boundary from the remaining monetary budget so it can reserve a conservative
estimate before sending content. Unlimited goals omit an application output-token
ceiling. Strict
workspace privacy is represented as a typed policy and sends both no-collection and
zero-data-retention routing constraints.

Configured chat routes remain separate from spending authority. In the
Goals menu, **Models** discovers each configured provider catalog without performing
inference and lets the user select a provider/model separately for lead, implementer,
and reviewer. A local default may run without a stored override. A remote default or
override must be explicitly selected for that goal, and its spend mode must be Unlimited
or Capped. Every agent call remains subject to that goal's monetary spend policy.
Selections persist by goal and role; changing one role does not authorize another.
Published input/output prices are shown per million tokens, and missing pricing is
shown explicitly because paid calls fail closed when the provider cannot price them.

`Routing:Embedding` selects the named module used for semantic indexing and query
embeddings independently of the three chat roles. `EmbeddingDimensions` is required
because vector dimensions are part of the durable partition identity; changing the
provider, model, dimensions, or chunking version never mixes incompatible vectors.

For temporary overrides, configuration key separators become double underscores:

```bash
HARNESS_Providers__Ollama__Endpoint=http://localhost:11434 \
dotnet run --project src/Harness.Host/Harness.Host.csproj
```

Provider credentials do not belong in XML. They remain references resolved through
Linux Secret Service with narrowly scoped environment fallback.
Harness.NET does not automatically load repository `.env` files. Environment
fallback names are case-sensitive on Linux and must match the configured
`ApiKeyEnvironmentVariable` exactly; the shipped OpenRouter name is
`OPENROUTER_API_KEY`.

The Avalonia **Settings → Model providers** page exposes each effective named module.
It can persist endpoint, chat and embedding defaults, embedding dimensions, connect
and request timeouts, and OpenRouter secret-reference changes into the XDG
`harness.xml` override while preserving unrelated XML configuration. Active provider
objects, routes, conversation clients, and semantic-index partition identity remain
fixed for the process lifetime, so the page marks these changes as requiring restart.
Environment variables and command-line configuration retain their documented higher
precedence after restart.

An OpenRouter API key entered in Settings is write-only: it is stored at the selected
Secret Service reference, cleared from the control, and never written to XML, SQLite,
logs, or a settings snapshot. The UI reports only `Missing`, `Configured`, or
`Unavailable`. Saving a key is still not permission by itself to make a paid request;
the selected goal's persisted spend mode supplies that authority.

Each child of `Framework/Rules` is a named typed rule. Higher precedence values are
more specific. A locked effective rule blocks later overrides; conflicting values at
the same precedence make the effective framework invalid until resolved.

## Operational modes

`--no-ui` initializes and migrates Harness.NET, prints its ready/schema status, and
exits. `--wait-for-shutdown` performs the same non-interactive initialization and
then waits for SIGINT or SIGTERM. These flags are host operations rather than
configuration keys and are removed before command-line configuration binding.

Avalonia is the default interactive frontend, including when no console streams are
attached. Use `--ui=terminal` to run the Terminal.Gui adapter or `--ui=avalonia` to
select the default explicitly. Terminal mode requires attached input and output.
`--ui` cannot be combined with backup, wait, or non-UI modes.

## User themes

The selected semantic theme ID is stored in application state. User palettes are
loaded from `$XDG_CONFIG_HOME/harness.net/themes/*.xml` (or the platform fallback for
`XDG_CONFIG_HOME`). A palette inherits a built-in Light or Dark base and overrides
only named color tokens:

```xml
<harnessTheme version="1" id="nord" name="Nord" base="dark">
  <color token="Window" value="#2E3440" />
  <color token="TextPrimary" value="#ECEFF4" />
  <color token="Accent" value="#88C0D0" />
</harnessTheme>
```

IDs use lowercase letters, digits, dots, underscores, and hyphens. Supported tokens
are the semantic names defined by `ThemeColorToken`; colors are opaque `#RRGGBB`.
Harness.NET reads at most 64 files of 64 KiB each, prohibits DTDs and external
resources, and excludes palettes that are malformed or fail required contrast.
Executable AXAML, fonts, includes, and external assets are never loaded from a theme.
Use Reload in the desktop theme selector after editing a file.
