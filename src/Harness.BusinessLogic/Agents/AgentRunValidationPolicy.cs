using Harness.BusinessLogic.Mutations;

namespace Harness.BusinessLogic.Agents;

internal static class AgentRunValidationPolicy
{
    internal static bool Succeeded(DotNetOperationView operation) =>
        operation.ErrorCode is null &&
        operation.ExitCode == 0 &&
        !operation.WasCancelled;

    internal static AgentRunResult Failure(AgentRole role, DotNetOperationView operation)
    {
        string detail = $"Deterministic {operation.Operation} failed with exit code " +
            $"{operation.ExitCode?.ToString() ?? "unknown"}.\n" +
            operation.StandardOutput + "\n" + operation.StandardError;
        const int maximumDetailCharacters = 16_000;
        if (detail.Length > maximumDetailCharacters)
        {
            detail = detail[^maximumDetailCharacters..];
        }

        return new(role, Output: null, new("agent_validation_failed"), new(detail));
    }
}
