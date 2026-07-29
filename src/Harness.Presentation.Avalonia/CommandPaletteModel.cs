namespace Harness.Presentation.Avalonia;

/// <summary>
/// One real, invocable application command. A command is only offered when it can
/// actually run; an unavailable command states why instead of failing on invoke.
/// </summary>
internal sealed record PaletteCommand(
    string Id,
    string Category,
    string Title,
    Func<ValueTask> InvokeAsync,
    string? Shortcut = null,
    string? UnavailableReason = null)
{
    internal bool IsAvailable => UnavailableReason is null;

    internal string Label => $"{Category}: {Title}";
}

/// <summary>
/// Ranks commands for the palette. Matching is a case-insensitive subsequence over the
/// "Category: Title" label, so "gitdiff" finds "Git: Open working-tree diff".
/// </summary>
internal static class CommandPaletteFilter
{
    internal static IReadOnlyList<PaletteCommand> Rank(
        IReadOnlyList<PaletteCommand> commands,
        string query)
    {
        ArgumentNullException.ThrowIfNull(commands);
        string trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            // Available commands first, then stable original order.
            return [.. commands.OrderByDescending(command => command.IsAvailable)];
        }

        return
        [
            .. commands
                .Select(command => (Command: command, Score: Score(command.Label, trimmed)))
                .Where(entry => entry.Score > 0)
                .OrderByDescending(entry => entry.Command.IsAvailable)
                .ThenByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Command.Label, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Command)
        ];
    }

    /// <summary>
    /// Returns 0 when the query is not a subsequence of the label. Contiguous and
    /// word-start matches rank above scattered ones.
    /// </summary>
    internal static int Score(string label, string query)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(query);
        int score = 0;
        int index = 0;
        bool previousMatched = false;

        foreach (char wanted in query)
        {
            if (char.IsWhiteSpace(wanted))
            {
                continue;
            }

            int found = IndexOf(label, wanted, index);
            if (found < 0)
            {
                return 0;
            }

            score += 1;
            if (found == 0 || !char.IsLetterOrDigit(label[found - 1]))
            {
                score += 4;
            }
            else if (previousMatched && found == index)
            {
                score += 2;
            }

            previousMatched = found == index;
            index = found + 1;
        }

        return score;
    }

    private static int IndexOf(string label, char wanted, int start)
    {
        for (int index = start; index < label.Length; index++)
        {
            if (char.ToUpperInvariant(label[index]) == char.ToUpperInvariant(wanted))
            {
                return index;
            }
        }

        return -1;
    }
}
