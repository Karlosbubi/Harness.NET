using Harness.BusinessLogic.Agents;
using Harness.DataAccess.Models.Ollama;
using Microsoft.Extensions.AI;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;

namespace Harness.BusinessLogic.Tests.Agents;

public sealed class AgentActivityLiveIntegrationTests
{
    [Fact]
    [Trait("Category", "OllamaLiveIntegration")]
    [Trait("Tier", "Live")]
    public async Task Local_stream_exposes_receiving_state_without_content()
    {
        if (Environment.GetEnvironmentVariable("HARNESS_RUN_OLLAMA_LIVE_TESTS") != "1")
        {
            return;
        }

        string endpoint = Environment.GetEnvironmentVariable("HARNESS_OLLAMA_ENDPOINT") ??
            "http://ollama.local.brunner.codes:11434";
        string model = Environment.GetEnvironmentVariable("HARNESS_OLLAMA_MODEL") ??
            "harness-ornith:9b-v1";
        using HttpClient httpClient = new(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            BaseAddress = new Uri(endpoint, UriKind.Absolute),
            Timeout = TimeSpan.FromMinutes(3),
        };
        AgentActivityService activities = new(TimeProvider.System);
        ModelProviderChatClient client = new(
            new OllamaModelProvider(httpClient, new(8_192)),
            new(model),
            remoteGoalId: null,
            AgentRole.Implementer,
            activityGoalId: new("live-activity-smoke"),
            activityService: activities);
        await using IAsyncEnumerator<ChatResponseUpdate> stream = client
            .GetStreamingResponseAsync([
                new Microsoft.Extensions.AI.ChatMessage(
                    AIChatRole.User,
                    "Reply with exactly HARNESS_ACTIVITY_OK."),
            ])
            .GetAsyncEnumerator();

        Assert.True(await stream.MoveNextAsync());
        AgentActivityView receiving = Assert.Single(activities.GetSnapshot().Items);
        Assert.Equal(AgentActivityPhase.ReceivingResponse, receiving.Phase);
        Assert.Equal("model_response", receiving.Operation.Value);
        Assert.DoesNotContain("HARNESS_ACTIVITY_OK", receiving.ToString(),
            StringComparison.Ordinal);

        while (await stream.MoveNextAsync())
        {
        }
        Assert.Equal(
            AgentActivityPhase.Completed,
            Assert.Single(activities.GetSnapshot().Items).Phase);
    }
}
