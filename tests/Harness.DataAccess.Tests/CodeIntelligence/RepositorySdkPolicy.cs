namespace Harness.DataAccess.Tests.CodeIntelligence;

internal static class RepositorySdkPolicy
{
    internal const string MinimumVersion = "10.0.100";
    internal const string RollForward = "latestFeature";

    internal const string GlobalJson = """
        {
          "sdk": {
            "version": "10.0.100",
            "rollForward": "latestFeature",
            "allowPrerelease": false
          }
        }
        """;
}
