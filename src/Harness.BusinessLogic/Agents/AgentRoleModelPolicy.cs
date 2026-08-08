namespace Harness.BusinessLogic.Agents;

internal static class AgentRoleModelPolicy
{
    private const string ToolsCapability = "tools";

    internal static IReadOnlyList<AgentRole> SupportedRoles(
        IReadOnlyList<string> capabilities) => Enum.GetValues<AgentRole>()
        .Where(role => Supports(role, capabilities))
        .ToArray();

    internal static bool Supports(
        AgentRole role,
        IReadOnlyList<string> capabilities) => RequiredCapabilities(role).All(required =>
        capabilities.Contains(required, StringComparer.OrdinalIgnoreCase));

    internal static IReadOnlyList<string> RequiredCapabilities(AgentRole role) => role switch
    {
        AgentRole.Lead => [ToolsCapability],
        AgentRole.Implementer => [ToolsCapability],
        AgentRole.Reviewer => [ToolsCapability],
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
