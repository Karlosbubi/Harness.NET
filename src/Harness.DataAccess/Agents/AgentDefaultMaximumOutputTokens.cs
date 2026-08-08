namespace Harness.DataAccess.Agents;

public sealed record AgentDefaultMaximumOutputTokens
{
    public const int MaximumValue = 10_000_000;

    public AgentDefaultMaximumOutputTokens(int value)
    {
        if (value is < 1 or > MaximumValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Agent default output tokens must be between 1 and {MaximumValue}.");
        }

        Value = value;
    }

    public int Value { get; }
}
