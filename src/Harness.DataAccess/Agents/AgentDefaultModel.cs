namespace Harness.DataAccess.Agents;

public sealed record AgentDefaultModel
{
    public AgentDefaultModel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An agent default model is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}
