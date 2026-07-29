namespace Harness.DataAccess.Agents;

public sealed record AgentDefaultProvider
{
    public AgentDefaultProvider(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An agent default provider is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}
