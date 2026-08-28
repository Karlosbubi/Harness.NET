using Harness.DataAccess.Configuration;
using Harness.Host.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;

namespace Harness.Host.Tests;

public sealed class ServiceRegistrationTests
{
    private const string PreSplitBaselineCommit = "16f3085";
    private const string PreSplitServiceInventoryFingerprint =
        "9A3F83881023EB185BBD19F7E6B2D3EB901D948384CB194791142E4008338365";

    [Fact]
    public void Feature_modules_match_the_reviewed_pre_split_registration_inventory()
    {
        ApplicationPaths current = new(
            "/tmp/harness-config",
            "/tmp/harness-data",
            "/tmp/harness-state",
            "/tmp/harness-cache",
            "/tmp/harness-data/harness.db",
            "/tmp/harness-state/logs",
            "/tmp/harness-state/worktrees");
        TestApplicationPaths paths = new(current);
        HarnessConfiguration configuration = HarnessConfigurationLoader.Load(
            [], current, AppContext.BaseDirectory);

        ServiceCollection services = new();
        List<(string Name, int Count)> actual = [];
        AddModule("Infrastructure", () =>
            services.AddHarnessInfrastructure(paths, configuration, evaluationRoot: null));
        AddModule("Integrations", () =>
            services.AddHarnessIntegrations(configuration, isEvaluation: false));
        AddModule("Workspace", () =>
            services.AddHarnessWorkspace(configuration));
        AddModule("Goals", () =>
            services.AddHarnessGoals(configuration));
        AddModule("Presentation", () =>
            services.AddHarnessPresentation());

        Assert.True(
            actual.SequenceEqual(
                new (string Name, int Count)[]
                {
                    ("Infrastructure", 14),
                    ("Integrations", 31),
                    ("Workspace", 70),
                    ("Goals", 14),
                    ("Presentation", 9),
                }),
            "Registration inventory changed: " +
            string.Join(", ", actual.Select(item => $"{item.Name}={item.Count}")));

        string serviceInventory = string.Join('\n', services
            .Select(descriptor => string.Join('|',
                descriptor.ServiceType.FullName,
                descriptor.ServiceKey,
                descriptor.Lifetime))
            .Order(StringComparer.Ordinal));
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            serviceInventory)));
        Assert.True(
            string.Equals(PreSplitServiceInventoryFingerprint, fingerprint, StringComparison.Ordinal),
            $"Registration inventory differs from pre-split commit {PreSplitBaselineCommit}: " +
            $"expected {PreSplitServiceInventoryFingerprint}, actual {fingerprint}.\n" +
            serviceInventory);

        void AddModule(string name, Action register)
        {
            int before = services.Count;
            register();
            actual.Add((name, services.Count - before));
        }
    }

    private sealed record TestApplicationPaths(ApplicationPaths Current) : IApplicationPaths;
}
