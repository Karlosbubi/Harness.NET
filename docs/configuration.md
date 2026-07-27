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
      <ConnectTimeoutSeconds>5</ConnectTimeoutSeconds>
      <RequestTimeoutSeconds>600</RequestTimeoutSeconds>
    </LocalCoding>
  </Providers>
  <Routing>
    <MainLlm>LocalCoding</MainLlm>
    <Reviewer>LocalCoding</Reviewer>
    <ToolLlm>LocalCoding</ToolLlm>
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
implementation types, so several differently configured modules may eventually use
the same provider implementation. All routes are validated at startup. `Ollama` is
the only implemented provider kind today; OpenRouter-named modules become valid when
that connector lands.

For temporary overrides, configuration key separators become double underscores:

```bash
HARNESS_Providers__Ollama__Endpoint=http://localhost:11434 \
dotnet run --project src/Harness.Host/Harness.Host.csproj
```

Provider credentials do not belong in XML. They remain references resolved through
Linux Secret Service with narrowly scoped environment fallback.

Each child of `Framework/Rules` is a named typed rule. Higher precedence values are
more specific. A locked effective rule blocks later overrides; conflicting values at
the same precedence make the effective framework invalid until resolved.

## Operational modes

`--no-ui` initializes and migrates Harness.NET, prints its ready/schema status, and
exits. `--wait-for-shutdown` performs the same non-interactive initialization and
then waits for SIGINT or SIGTERM. These flags are host operations rather than
configuration keys and are removed before command-line configuration binding.
