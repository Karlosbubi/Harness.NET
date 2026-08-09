using System.Text.Json;

namespace Harness.BusinessLogic.Workflows;

internal static class GoalReviewParser
{
    internal static GoalReviewResult Parse(string value)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                StructuredAgentOutput.NormalizeJson(value));
            JsonElement root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object ||
                !root.TryGetProperty("decision", out JsonElement decisionElement) ||
                !root.TryGetProperty("summary", out JsonElement summaryElement))
            {
                return Failure("The reviewer response must contain decision and summary fields.");
            }

            GoalReviewDecision? decision = decisionElement.GetString() switch
            {
                "accept" => GoalReviewDecision.Accept,
                "revise" => GoalReviewDecision.Revise,
                _ => null,
            };
            string? summary = summaryElement.GetString()?.Trim();
            return decision is null || string.IsNullOrWhiteSpace(summary)
                ? Failure("The reviewer decision must be accept or revise with a non-empty summary.")
                : new(decision, summary, Error: null);
        }
        catch (JsonException exception)
        {
            return Failure($"The reviewer response is not valid JSON: {exception.Message}");
        }
    }

    private static GoalReviewResult Failure(string error) =>
        new(Decision: null, Summary: null, error);
}
