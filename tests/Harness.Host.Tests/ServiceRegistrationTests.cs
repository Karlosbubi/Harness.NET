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
    private static readonly IReadOnlySet<string> Task071GoalRegistrations = new HashSet<string>(
        [
            "Harness.BusinessLogic.Agents.AgentActivityService",
            "Harness.BusinessLogic.Agents.IAgentActivityReader",
        ],
        StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> Task052CoverageRegistrations = new HashSet<string>(
        [
            "Harness.DataAccess.Coverage.IWorkspaceCoverageReader",
            "Harness.DataAccess.Coverage.IDeveloperCoverageStore",
            "Harness.BusinessLogic.Coverage.IDeveloperCoverageService",
        ],
        StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> Task052DebuggerRegistrations = new HashSet<string>(
        [
            "Harness.BusinessLogic.Debugging.IDeveloperDebuggerService",
            "Harness.BusinessLogic.Debugging.IDeveloperDebuggerSettingsService",
            "Harness.BusinessLogic.Execution.IDeveloperExecutionTargetResolver",
            "Harness.BusinessLogic.Execution.DeveloperProjectExecutionService",
            "Harness.DataAccess.Debugging.IDebugAdapterExecutableResolver",
            "Harness.DataAccess.Debugging.IDebugAdapterPackageStore",
            "Harness.DataAccess.Debugging.IDebugAdapterSessionFactory",
            "Harness.DataAccess.Debugging.IDotNetDebugSessionFactory",
            "Harness.DataAccess.Debugging.ManagedDebugAdapterPackageStore",
            "Harness.DataAccess.Debugging.NetCoreDbgAdapterSessionFactory",
        ],
        StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ReviewedFeatureRegistrations = new HashSet<string>(
        Task071GoalRegistrations.Concat(Task052CoverageRegistrations)
            .Concat(Task052DebuggerRegistrations), StringComparer.Ordinal);

    [Fact]
    public void Feature_modules_preserve_the_baseline_plus_reviewed_feature_registrations()
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
                    ("Workspace", 83),
                    ("Goals", 16),
                    ("Presentation", 9),
                }),
            "Registration inventory changed: " +
            string.Join(", ", actual.Select(item => $"{item.Name}={item.Count}")));

        ServiceDescriptor[] reviewed = services
            .Where(descriptor => ReviewedFeatureRegistrations.Contains(
                descriptor.ServiceType.FullName ?? string.Empty))
            .ToArray();
        Assert.Equal(ReviewedFeatureRegistrations.Count, reviewed.Length);
        Assert.All(reviewed, descriptor =>
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));

        string serviceInventory = string.Join('\n', services
            .Where(descriptor => !ReviewedFeatureRegistrations.Contains(
                descriptor.ServiceType.FullName ?? string.Empty))
            .Select(descriptor => string.Join('|',
                descriptor.ServiceType.FullName,
                descriptor.ServiceKey,
                descriptor.Lifetime))
            .Order(StringComparer.Ordinal));
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            serviceInventory)));
        Assert.True(
            string.Equals(PreSplitServiceInventoryFingerprint, fingerprint, StringComparison.Ordinal),
            $"Registration inventory beyond Task 071 differs from pre-split commit " +
            $"{PreSplitBaselineCommit}: " +
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
