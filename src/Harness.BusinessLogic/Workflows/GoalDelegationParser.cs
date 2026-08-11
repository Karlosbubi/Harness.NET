using System.Text.Json;
using Harness.BusinessLogic.Agents;

namespace Harness.BusinessLogic.Workflows;

internal static class GoalDelegationParser
{
    private const int MaximumPlanCharacters = 32_000;
    private const int MaximumTaskCount = 12;

    internal static GoalDelegation Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Failure(
                "The Lead returned no plan. Retry with another Lead model or add guidance.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                StructuredAgentOutput.NormalizeJson(value));
            JsonElement root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object ||
                !ExactProperties(root, "plan", "tasks") ||
                !root.TryGetProperty("plan", out JsonElement planElement) ||
                planElement.ValueKind is not JsonValueKind.String ||
                !root.TryGetProperty("tasks", out JsonElement tasksElement) ||
                tasksElement.ValueKind is not JsonValueKind.Array)
            {
                return Failure("Lead output must contain exactly 'plan' and 'tasks'.");
            }

            string? plan = planElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(plan) || plan.Length > MaximumPlanCharacters ||
                tasksElement.GetArrayLength() is < 1 or > MaximumTaskCount)
            {
                return Failure("A bounded plan and 1-12 delegated tasks are required.");
            }

            List<GoalDelegatedTask> tasks = [];
            int ignoredDiscoveryTasks = 0;
            int taskIndex = 0;
            foreach (JsonElement task in tasksElement.EnumerateArray())
            {
                taskIndex++;
                if (task.ValueKind is not JsonValueKind.Object)
                {
                    return InvalidTask(taskIndex, "must be a JSON object");
                }

                if (!ExactProperties(task, "title", "objective", "fileAreas",
                        "acceptanceCriteria"))
                {
                    return InvalidTask(taskIndex,
                        "must contain exactly title, objective, fileAreas, and acceptanceCriteria");
                }

                if (!Text(task, "title", 256, out string title))
                {
                    return InvalidTask(taskIndex, "title must contain 1-256 characters");
                }

                if (!Text(task, "objective", 8_192, out string objective))
                {
                    return InvalidTask(taskIndex, "objective must contain 1-8192 characters");
                }

                if (!StringList(task, "fileAreas", 32, 512, out string fileAreas))
                {
                    return InvalidTask(taskIndex,
                        "fileAreas must contain 1-32 valid repository-relative paths");
                }

                if (!StringList(task, "acceptanceCriteria", 32, 512,
                        out string acceptanceCriteria))
                {
                    return InvalidTask(taskIndex,
                        "acceptanceCriteria must contain 1-32 non-empty values");
                }

                if (IsStandaloneDiscoveryTask(title, objective))
                {
                    ignoredDiscoveryTasks++;
                    continue;
                }

                tasks.Add(new(new(title), new(objective), new(fileAreas),
                    new(acceptanceCriteria)));
            }

            if (tasks.Count == 0)
            {
                return Failure(
                    "Standalone discovery, inspection, planning, and validation tasks are not " +
                    "delegated work. Fold required inspection and validation into an implementation slice.");
            }

            if (ignoredDiscoveryTasks > 0)
            {
                plan += $"\n\nHarness normalization: ignored {ignoredDiscoveryTasks} standalone " +
                    "discovery/inspection/planning/validation task(s); implementers inspect and " +
                    "validate within the retained durable slices.";
            }

            return new(plan, tasks, Error: null);
        }
        catch (JsonException exception)
        {
            return Failure($"Lead output is not valid delegation JSON: {exception.Message}");
        }
    }

    private static bool ExactProperties(JsonElement element, params string[] expected)
    {
        string[] actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        return actual.Length == expected.Length &&
            expected.All(name => actual.Contains(name, StringComparer.Ordinal));
    }

    private static bool IsStandaloneDiscoveryTask(string title, string objective)
    {
        string combined = $"{title} {objective}";
        string[] nonMutationWords =
        [
            "inspect", "analyze", "analyse", "discover", "explore", "inventory", "assess",
            "plan", "planning", "build", "test", "verify", "validate", "review", "document",
            "run", "execute", "check",
        ];
        string[] strongMutationWords =
            ["implement", "update", "change", "fix", "replace", "remove", "refactor", "integrate"];
        string[] weakMutationWords = ["create", "add", "write"];
        bool hasNonMutationWord = nonMutationWords.Any(word => ContainsWord(combined, word));
        bool hasStrongMutation = strongMutationWords.Any(word => ContainsWord(combined, word));
        bool titleHasStrongMutation = strongMutationWords.Any(word => ContainsWord(title, word));
        bool titleStartsWithNonMutation = nonMutationWords.Any(word =>
            StartsWithWord(title, word));
        bool hasWeakMutation = weakMutationWords.Any(word => ContainsWord(combined, word));
        bool isPlanning = ContainsWord(combined, "plan") || ContainsWord(combined, "planning");
        return titleStartsWithNonMutation && !titleHasStrongMutation ||
            hasNonMutationWord && !hasStrongMutation && (!hasWeakMutation || isPlanning);
    }

    private static bool StartsWithWord(string value, string word) =>
        value.TrimStart().StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
        (value.TrimStart().Length == word.Length ||
            !char.IsLetterOrDigit(value.TrimStart()[word.Length]));

    private static bool ContainsWord(string value, string word) =>
        value.Split([' ', '\t', '\r', '\n', ',', '.', ':', ';', '-', '_', '/', '(', ')'],
                StringSplitOptions.RemoveEmptyEntries)
            .Contains(word, StringComparer.OrdinalIgnoreCase);

    private static bool Text(
        JsonElement element,
        string property,
        int maximumCharacters,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out JsonElement child) ||
            child.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        value = child.GetString()?.Trim() ?? string.Empty;
        return value.Length is > 0 && value.Length <= maximumCharacters;
    }

    private static bool StringList(
        JsonElement element,
        string property,
        int maximumItems,
        int maximumItemCharacters,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out JsonElement child) ||
            child.ValueKind is not JsonValueKind.Array ||
            child.GetArrayLength() is < 1 || child.GetArrayLength() > maximumItems)
        {
            return false;
        }

        string[] items = child.EnumerateArray()
            .Select(item => item.ValueKind is JsonValueKind.String
                ? item.GetString()?.Trim() ?? string.Empty
                : string.Empty)
            .ToArray();
        if (items.Any(item => item.Length is 0 || item.Length > maximumItemCharacters))
        {
            return false;
        }

        if (property == "fileAreas" && items.Any(item => !AgentToolFactory.ValidFileArea(item)))
        {
            return false;
        }

        value = string.Join("\n", items.Select(item => $"- {item}"));
        return true;
    }

    private static GoalDelegation Failure(string error) => new(null, [], error);

    private static GoalDelegation InvalidTask(int index, string error) =>
        Failure($"Lead task {index} {error}.");
}
