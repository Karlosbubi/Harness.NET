namespace Harness.BusinessLogic.Agents;

public sealed record MaximumAgentOutputTokens(int Value)
{
    public const int MaximumValue = 10_000_000;
}
