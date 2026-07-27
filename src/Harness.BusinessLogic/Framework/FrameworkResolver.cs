namespace Harness.BusinessLogic.Framework;

internal sealed class FrameworkResolver : IFrameworkResolver
{
    public FrameworkResolution Resolve(IReadOnlyList<FrameworkRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        List<FrameworkIssue> issues = [];
        FrameworkRule[] validRules = rules
            .Where(rule => Validate(rule, issues))
            .ToArray();
        List<EffectiveFrameworkRule> effectiveRules = [];

        foreach (IGrouping<string, FrameworkRule> ruleGroup in validRules
                     .GroupBy(rule => rule.Key, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            EffectiveFrameworkRule? effective = ResolveRule(ruleGroup, issues);
            if (effective is not null)
            {
                effectiveRules.Add(effective);
            }
        }

        return new(effectiveRules, issues);
    }

    private static EffectiveFrameworkRule? ResolveRule(
        IEnumerable<FrameworkRule> rules,
        ICollection<FrameworkIssue> issues)
    {
        EffectiveFrameworkRule? effective = null;
        foreach (IGrouping<int, FrameworkRule> precedenceGroup in rules
                     .GroupBy(rule => rule.Precedence)
                     .OrderBy(group => group.Key))
        {
            FrameworkRule[] candidates = precedenceGroup
                .OrderByDescending(rule => rule.IsLocked)
                .ThenBy(rule => rule.Source, StringComparer.Ordinal)
                .ToArray();
            string[] distinctValues = candidates
                .Select(rule => rule.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (distinctValues.Length > 1)
            {
                issues.Add(new(
                    "same_level_conflict",
                    $"Rule '{candidates[0].Key}' has conflicting values at layer " +
                    $"'{candidates[0].Layer}'.",
                    candidates[0].Key,
                    candidates.Select(rule => rule.Source).Distinct().ToArray()));
                return null;
            }

            FrameworkRule candidate = candidates[0];
            if (effective is { IsLocked: true })
            {
                if (!string.Equals(effective.Value, candidate.Value, StringComparison.Ordinal))
                {
                    issues.Add(new(
                        "locked_override",
                        $"Locked rule '{effective.Key}' from '{effective.Source}' blocked " +
                        $"an override from '{candidate.Source}'.",
                        effective.Key,
                        [effective.Source, candidate.Source]));
                }

                continue;
            }

            effective = new(
                candidate.Key,
                candidate.Value,
                candidate.Layer,
                candidates.Any(rule => rule.IsLocked),
                candidate.Source);
        }

        return effective;
    }

    private static bool Validate(FrameworkRule? rule, ICollection<FrameworkIssue> issues)
    {
        if (rule is not null &&
            !string.IsNullOrWhiteSpace(rule.Key) &&
            !string.IsNullOrWhiteSpace(rule.Value) &&
            !string.IsNullOrWhiteSpace(rule.Layer) &&
            !string.IsNullOrWhiteSpace(rule.Source) &&
            rule.Precedence >= 0)
        {
            return true;
        }

        issues.Add(new(
            "invalid_rule",
            "Framework rules require a key, value, non-negative precedence, layer, and source.",
            rule?.Key,
            string.IsNullOrWhiteSpace(rule?.Source) ? [] : [rule.Source]));
        return false;
    }
}
