namespace Harness.BusinessLogic.Agents;

internal static class AgentRunResultPolicy
{
    internal static AgentRunResult Final(
        AgentRole role,
        string? output,
        int toolCallCount)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            return new(role, new(output), ErrorCode: null, Error: null);
        }

        if (role is AgentRole.Implementer && toolCallCount > 0)
        {
            return new(
                role,
                new("Completed the bounded task through typed tools; inspect durable tool " +
                    "evidence for exact operations and validation."),
                ErrorCode: null,
                Error: null);
        }

        return new(
            role,
            Output: null,
            new("empty_agent_response"),
            new("The model returned no bounded role output."));
    }
}
