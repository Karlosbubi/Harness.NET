using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Harness.BusinessLogic.Agents;

namespace Harness.Presentation.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ModelProviderSettingsControlTests
{
    [Fact]
    public async Task Exposes_local_context_configuration_and_a_write_only_remote_key()
    {
        using ProviderSettingsService providers = new();
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore(providers);
        await store.LoadAsync(CancellationToken.None);
        Assert.Equal(2, store.Current.Settings.ProviderSettings!.Providers.Count);

        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            SettingsWindow window = new(store, CancellationToken.None);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ListBox categories = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>());
            categories.SelectedItem = SettingsCatalog.All.Single(category =>
                category.Id is SettingsCategoryId.ModelProviders);
            Dispatcher.UIThread.RunJobs();

            TextBox endpoint = Assert.Single(window.GetLogicalDescendants().OfType<TextBox>(), field =>
                AutomationProperties.GetName(field) == "OpenRouter endpoint");
            TextBox credential = Assert.Single(window.GetLogicalDescendants().OfType<TextBox>(), field =>
                AutomationProperties.GetName(field) == "OpenRouter API key");
            NumericUpDown context = Assert.Single(
                window.GetLogicalDescendants().OfType<NumericUpDown>(),
                field => string.Equals(
                    AutomationProperties.GetName(field),
                    "Ollama maximum agent context tokens",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal("https://openrouter.ai", endpoint.Text);
            Assert.Equal('●', credential.PasswordChar);
            Assert.Equal(8_192m, context.Value);
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Save provider configuration"));
            Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                Equals(button.Content, "Replace API key"));
            Assert.DoesNotContain("sk-private", string.Join('\n', window.GetLogicalDescendants()
                .OfType<TextBlock>().Select(block => block.Text)), StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class ProviderSettingsService : IModelProviderSettingsService, IDisposable
    {
        private readonly ModelProviderSettingsSnapshot snapshot = new([
            new(
                new("Ollama"),
                AgentModelProviderKind.Ollama,
                new("http://localhost:11434"),
                new("gemma4:latest"),
                new("embeddinggemma"),
                new(768),
                new(8_192),
                new(5),
                new(600),
                SecretName: null,
                EnvironmentVariable: null,
                ModelProviderCredentialState.NotApplicable,
                CredentialMessage: null,
                RequiresRestart: false),
            new(
                new("OpenRouter"),
                AgentModelProviderKind.OpenRouter,
                new("https://openrouter.ai"),
                new("openai/gpt-5-mini"),
                new("openai/text-embedding-3-small"),
                new(1536),
                MaximumAgentContextTokens: null,
                new(10),
                new(600),
                new("openrouter-api-key"),
                new("OPENROUTER_API_KEY"),
                ModelProviderCredentialState.Configured,
                CredentialMessage: null,
                RequiresRestart: false),
        ]);

        public ValueTask<ModelProviderSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(snapshot);

        public ValueTask<ModelProviderSettingsResult> UpdateAsync(
            ModelProviderSettingsUpdate request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ModelProviderSettingsResult(snapshot, null, null));

        public ValueTask<ModelProviderSettingsResult> SetCredentialAsync(
            ModelProviderCredentialUpdate request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ModelProviderSettingsResult(snapshot, null, null));

        public void Dispose()
        {
        }
    }
}
