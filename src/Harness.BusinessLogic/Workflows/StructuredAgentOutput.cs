namespace Harness.BusinessLogic.Workflows;

internal static class StructuredAgentOutput
{
    internal static string NormalizeJson(string value)
    {
        string trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) ||
            !trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        int openingEnd = trimmed.IndexOf('\n');
        if (openingEnd < 0)
        {
            return trimmed;
        }

        string opening = trimmed[..openingEnd].Trim();
        if (!opening.Equals("```", StringComparison.Ordinal) &&
            !opening.Equals("```json", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        string candidate = trimmed[(openingEnd + 1)..^3].Trim();
        return candidate.Contains("```", StringComparison.Ordinal)
            ? trimmed
            : candidate;
    }
}
