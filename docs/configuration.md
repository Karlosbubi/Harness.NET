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
remote budget. Explicit goal-role selection authorizes planning inference before plan
approval without granting repository mutation; plan approval separately authorizes
the isolated worktree capabilities. Chat requests require a positive
maximum-output-token value so the
connector can reserve a conservative estimate before sending content. Strict
workspace privacy is represented as a typed policy and sends both no-collection and
zero-data-retention routing constraints.

Configured chat routes are defaults, not implicit remote spending authority. In the
Goals menu, **Models** discovers each configured provider catalog without performing
inference and lets the user select a provider/model separately for lead, implementer,
and reviewer. A local default may run without a stored override. A remote default or
override must be explicitly selected for that goal, the goal must have a positive
remote cap, and every agent call must declare a positive output-token maximum.
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

Each child of `Framework/Rules` is a named typed rule. Higher precedence values are
more specific. A locked effective rule blocks later overrides; conflicting values at
the same precedence make the effective framework invalid until resolved.

## Operational modes

`--no-ui` initializes and migrates Harness.NET, prints its ready/schema status, and
exits. `--wait-for-shutdown` performs the same non-interactive initialization and
then waits for SIGINT or SIGTERM. These flags are host operations rather than
configuration keys and are removed before command-line configuration binding.
