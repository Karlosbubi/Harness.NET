using System.Net;
using System.Text;
using System.Text.Json;
using Harness.DataAccess.Mcp;
using Harness.DataAccess.Research;

namespace Harness.DataAccess.Tests.Research;

public sealed class RemoteDocumentationSourcesTests
{
    [Fact]
    public async Task Mcp_source_invokes_only_configured_closed_read_only_tool_and_maps_citations()
    {
        McpToolDefinition tool = Tool(isOpenWorld: false);
        FakeMcpClient client = new(tool, """
            { "structuredContent": { "results": [ {
              "title": "Compiled bindings", "content": "Use x:DataType.",
              "version": "12.1.0", "citation": "https://docs.avaloniaui.net/binding",
              "confidence": 0.9
            } ] } }
            """);
        McpDocumentationSource source = new(client,
            new StaticSettings(Settings([new("docs", "search_docs")], [])),
            TimeProvider.System);

        DocumentationSourceResult result = await source.SearchAsync(Query());

        Assert.True(result.IsSufficient);
        Assert.Equal(1, client.Calls);
        DocumentationSourceMatch match = Assert.Single(result.Matches);
        Assert.True(match.IsExactVersion);
        Assert.Equal("https://docs.avaloniaui.net/binding", match.Citation.Value);
    }

    [Fact]
    public async Task Mcp_source_refuses_configured_open_world_tool()
    {
        FakeMcpClient client = new(Tool(isOpenWorld: true), "{}");
        McpDocumentationSource source = new(client,
            new StaticSettings(Settings([new("docs", "search_docs")], [])),
            TimeProvider.System);

        DocumentationSourceResult result = await source.SearchAsync(Query());

        Assert.Equal(0, client.Calls);
        Assert.Equal("mcp_documentation_incomplete", result.ErrorCode);
    }

    [Fact]
    public async Task Web_source_sends_only_bounded_research_terms_and_maps_result()
    {
        FakeHttpHandler handler = new();
        HttpDocumentationSource source = new(new HttpClient(handler),
            new StaticSettings(Settings([], [new(new Uri("https://learn.example.test/api/search"))])),
            TimeProvider.System);

        DocumentationSourceResult result = await source.SearchAsync(Query());

        Assert.True(result.IsSufficient);
        Assert.DoesNotContain("workspace", handler.Request!.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Avalonia", Uri.UnescapeDataString(handler.Request.Query), StringComparison.Ordinal);
        Assert.Equal("https://docs.example.test/binding", Assert.Single(result.Matches).Citation.Value);
    }

    private static DocumentationSourceQuery Query() => new(
        new("Avalonia"), new("12.1.0"), new("compiled binding"), 5, 12_000);

    private static McpToolDefinition Tool(bool isOpenWorld)
    {
        using JsonDocument schema = JsonDocument.Parse("{\"type\":\"object\"}");
        return new(new("docs"), new("search_docs"), "Search docs", "Search documentation",
            schema.RootElement.Clone(), null, true, false, isOpenWorld, true, null);
    }

    private static ResearchSourceSettings Settings(
        IReadOnlyList<DocumentationMcpToolRoute> tools,
        IReadOnlyList<DocumentationWebEndpoint> web) => new(
        true, true, true, true, false, [], tools, web,
        [new(new Uri("https://api.nuget.org/v3/index.json"))],
        ResearchRefreshPolicy.OnDemand, 5, 12_000, TimeSpan.FromDays(7), TimeSpan.FromDays(30));

    private sealed class FakeMcpClient(McpToolDefinition tool, string json) : IMcpToolClient
    {
        internal int Calls { get; private set; }
        public McpDiscoverySnapshot Current { get; } = new([
            new(new(new("docs"), new(new Uri("https://docs.example.test/mcp")),
                new(TimeSpan.FromSeconds(30)), true, false), "2026-07-28", [tool], null, null),
        ]);

        public ValueTask<McpDiscoverySnapshot> DiscoverAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Current);

        public ValueTask<McpToolInvocationResult> InvokeAsync(McpToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal("Avalonia", invocation.Arguments["library"]);
            Assert.Equal("12.1.0", invocation.Arguments["version"]);
            return ValueTask.FromResult(new McpToolInvocationResult(json, false, null, null));
        }
    }

    private sealed class StaticSettings(ResearchSourceSettings value) : IResearchSettingsStore
    {
        public ValueTask<ResearchSourceSettings> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(value);
        public ValueTask SaveAsync(ResearchSourceSettings settings,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        internal Uri? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    { "results": [ {
                      "title": "Compiled bindings", "description": "Use x:DataType.",
                      "url": "https://docs.example.test/binding", "version": "12.1.0"
                    } ] }
                    """, Encoding.UTF8, "application/json"),
            });
        }
    }
}
