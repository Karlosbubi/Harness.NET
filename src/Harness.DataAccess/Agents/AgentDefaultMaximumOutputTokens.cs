namespace Harness.DataAccess.Agents;

public sealed record AgentDefaultMaximumOutputTokens
{
    public AgentDefaultMaximumOutputTokens(int value)
    {
        if (value is < 1 or > 8192)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Agent default output tokens must be between 1 and 8192.");
        }

        Value = value;
    }

    public int Value { get; }
}
