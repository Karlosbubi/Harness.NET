using System.Text;
using Harness.BusinessLogic.Framework;

namespace Harness.Presentation.Terminal;

internal static class FrameworkTextFormatter
{
    internal static string Format(FrameworkSnapshot snapshot)
    {
        StringBuilder text = new();
        text.AppendLine(snapshot.IsValid ? "VALID" : "ATTENTION REQUIRED");
        text.AppendLine();
        text.AppendLine("EFFECTIVE RULES");
        if (snapshot.Rules.Count == 0)
        {
            text.AppendLine("(none)");
        }

        foreach (EffectiveFrameworkRule rule in snapshot.Rules)
        {
            text.Append(rule.IsLocked ? "[locked] " : "          ")
                .Append(rule.Key)
                .Append(" = ")
                .AppendLine(rule.Value);
            text.Append("  ")
                .Append(rule.Layer)
                .Append(" | ")
                .AppendLine(rule.Source);
        }

        text.AppendLine();
        text.AppendLine("GUIDANCE DOCUMENTS");
        if (snapshot.Documents.Count == 0)
        {
            text.AppendLine("(none)");
        }

        foreach (FrameworkDocumentView document in snapshot.Documents)
        {
            text.Append('[')
                .Append(document.Layer)
                .Append(document.IsPrivate ? " | private" : " | shared")
                .Append("] ")
                .AppendLine(document.Source);
            text.AppendLine(document.Content.Trim());
            text.AppendLine();
        }

        if (snapshot.Issues.Count > 0)
        {
            text.AppendLine("ISSUES");
            foreach (FrameworkIssue issue in snapshot.Issues)
            {
                text.Append('[')
                    .Append(issue.Code)
                    .Append("] ")
                    .AppendLine(issue.Message);
            }
        }

        return text.ToString().TrimEnd();
    }
}
