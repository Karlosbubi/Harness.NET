using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Harness.BusinessLogic.Agents;

internal sealed partial class AgentRoleRunner
{
    private async ValueTask<AgentRunResult> ApplyStructuredLocalFileEditAsync(
        AgentRole role,
        GoalId goalId,
        string output,
        BootstrapInspection bootstrap,
        IList<AITool> tools,
        string? proposalBaseContent,
        CancellationToken cancellationToken)
    {
        string? content = StructuredProposalContent(
            output,
            proposalBaseContent ?? bootstrap.Content);

        content = PreserveSourceEnvelope(content, bootstrap);
        string? identityError = ValidateSourceIdentity(content, bootstrap);
        if (identityError is not null)
        {
            return new(role, Output: null, new("structured_source_identity_mismatch"),
                new(identityError));
        }
        if (string.IsNullOrWhiteSpace(content) ||
            tools.OfType<AIFunction>().SingleOrDefault(tool => tool.Name == "apply_file_edit") is not
            { } applyFileEdit)
        {
            return new(role, Output: null, new("invalid_structured_file_edit"),
                new("The local model returned an empty file proposal or no typed edit tool is available."));
        }

        string correlationId = "structured-local-edit-" + Guid.NewGuid().ToString("N");
        object? result = mutationService is null
            ? await applyFileEdit.InvokeAsync(
                new AIFunctionArguments
                {
                    ["correlationId"] = correlationId,
                    ["relativePath"] = bootstrap.RelativePath,
                    ["expectedSha256"] = bootstrap.Sha256,
                    ["content"] = content,
                },
                cancellationToken)
            : await mutationService.ApplyFileEditAsync(new(
                goalId.Value,
                new ToolCorrelationId(correlationId),
                bootstrap.RelativePath,
                bootstrap.Sha256,
                content,
                FileEditOrigin.Model), cancellationToken);
        FileEditView? edit = result as FileEditView;
        if (edit is null && result is not null)
        {
            edit = JsonSerializer.Deserialize<FileEditView>(
                JsonSerializer.Serialize(result, result.GetType()),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        if (edit is not { ErrorCode: null })
        {
            string detail = edit is null
                ? result is null
                    ? "No edit result was returned."
                    : JsonSerializer.Serialize(result, result.GetType())
                : DescribeRejectedEdit(edit);
            return new(role, Output: null, new("structured_file_edit_rejected"), new(detail));
        }


        if (mutationService is not null)
        {
            DotNetOperationView build = await mutationService.RunDotNetAsync(new(
                goalId.Value,
                new ToolCorrelationId("structured-build-" + Guid.NewGuid().ToString("N")),
                DotNetOperation.Build), cancellationToken);
            if (!AgentRunValidationPolicy.Succeeded(build))
            {
                return AgentRunValidationPolicy.Failure(role, build);
            }

            // A warning-free build proves only that the candidate compiles. Run the repository's
            // deterministic tests after every C# edit so a production task cannot be marked
            // complete while an existing behavioral contract is already failing. The correction
            // loop still owns the exact active file, so failures are repaired at their source.
            if (bootstrap.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                DotNetOperationView test = await mutationService.RunDotNetAsync(new(
                    goalId.Value,
                    new ToolCorrelationId("structured-test-" + Guid.NewGuid().ToString("N")),
                    DotNetOperation.Test), cancellationToken);
                if (!AgentRunValidationPolicy.Succeeded(test))
                {
                    return AgentRunValidationPolicy.Failure(role, test);
                }
            }
        }

        return new(role, new(JsonSerializer.Serialize(new
        {
            status = "complete",
            summary = $"Applied the typed edit to {edit.Path}.",
            validation = edit.AppliedCodeValidation is null
                ? Array.Empty<string>()
                : ["Deterministic code validation completed."],
            remaining = Array.Empty<string>(),
        })), ErrorCode: null, Error: null);
    }

    private static string? StructuredProposalContent(string output, string? repairBase)
    {
        string proposal = output.Trim();
        if (proposal.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLine = proposal.IndexOf('\n');
            int closingFence = proposal.LastIndexOf("```", StringComparison.Ordinal);
            return firstLine >= 0 && closingFence > firstLine
                ? proposal[(firstLine + 1)..closingFence].Trim()
                : null;
        }
        else if (proposal.StartsWith('{'))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(proposal);
                return document.RootElement.GetProperty("content").GetString();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        if (!proposal.StartsWith("<<<<<<< SEARCH", StringComparison.Ordinal))
        {
            // The structured local path deliberately suppresses tools and accepts only an
            // unambiguous machine-readable proposal. Treat narration, Markdown headings, and
            // other plain text as a format failure instead of feeding it to Roslyn as C#.
            return null;
        }

        if (string.IsNullOrEmpty(repairBase))
        {
            return null;
        }

        const string replacementPattern =
            @"<<<<<<< SEARCH\r?\n(?<search>.*?)\r?\n=======\r?\n(?<replacement>.*?)\r?\n>>>>>>> REPLACE";
        MatchCollection replacements = Regex.Matches(
            proposal,
            replacementPattern,
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (replacements.Count is < 1 or > 4 ||
            !string.IsNullOrWhiteSpace(Regex.Replace(
                proposal,
                replacementPattern,
                string.Empty,
                RegexOptions.Singleline | RegexOptions.CultureInvariant)))
        {
            return null;
        }

        string content = repairBase;
        foreach (Match replacement in replacements)
        {
            string search = MatchLineEndings(
                replacement.Groups["search"].Value,
                content);
            string replaceWith = MatchLineEndings(
                replacement.Groups["replacement"].Value,
                content);
            if (string.IsNullOrEmpty(search))
            {
                return null;
            }

            int first = content.IndexOf(search, StringComparison.Ordinal);
            if (first < 0 ||
                content.IndexOf(search, first + search.Length, StringComparison.Ordinal) >= 0)
            {
                return null;
            }

            content = content[..first] + replaceWith + content[(first + search.Length)..];
        }

        return content;
    }

    private static string MatchLineEndings(string value, string source)
    {
        string normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        return source.Contains("\r\n", StringComparison.Ordinal)
            ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal)
            : normalized;
    }

    private static string? PreserveSourceEnvelope(
        string? proposal,
        BootstrapInspection bootstrap)
    {
        if (string.IsNullOrWhiteSpace(proposal) ||
            !bootstrap.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            proposal.Contains("namespace ", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(bootstrap.Content))
        {
            return proposal;
        }

        string typeName = Path.GetFileNameWithoutExtension(bootstrap.RelativePath);
        string declarationPattern =
            "(?m)^[ \\t]*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|readonly|ref|file)[ \\t]+)*(?:class|record(?:[ \\t]+class)?|struct|interface|enum)[ \\t]+" +
            Regex.Escape(typeName) + "\\b";
        Match baselineDeclaration = Regex.Match(
            bootstrap.Content,
            declarationPattern,
            RegexOptions.CultureInvariant);
        Match proposalDeclaration = Regex.Match(
            proposal,
            declarationPattern,
            RegexOptions.CultureInvariant);
        if (baselineDeclaration.Success && proposalDeclaration.Success)
        {
            return bootstrap.Content[..baselineDeclaration.Index] +
                proposal[proposalDeclaration.Index..].Trim();
        }

        Match namespaceDeclaration = Regex.Match(
            bootstrap.Content,
            @"(?m)^[ \t]*namespace[ \t]+[^;{]+;[ \t]*(?:\r?\n)?",
            RegexOptions.CultureInvariant);
        return namespaceDeclaration.Success
            ? bootstrap.Content[..(namespaceDeclaration.Index + namespaceDeclaration.Length)] +
              proposal.Trim()
            : proposal;
    }

    private static string? ValidateSourceIdentity(
        string? proposal,
        BootstrapInspection bootstrap)
    {
        if (string.IsNullOrWhiteSpace(proposal) ||
            !bootstrap.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(bootstrap.Content))
        {
            return null;
        }

        Match baselineNamespace = Regex.Match(
            bootstrap.Content,
            @"(?m)^[ \t]*namespace[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_.]*)[ \t]*[;{]",
            RegexOptions.CultureInvariant);
        if (baselineNamespace.Success && !Regex.IsMatch(
                proposal,
                @"(?m)^[ \t]*namespace[ \t]+" +
                Regex.Escape(baselineNamespace.Groups["name"].Value) + @"[ \t]*[;{]",
                RegexOptions.CultureInvariant))
        {
            return "The proposal is for the wrong C# namespace. Preserve namespace " +
                baselineNamespace.Groups["name"].Value + " from the exact target file.";
        }

        const string typePattern =
            @"(?m)^[ \t]*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|readonly|ref|file)[ \t]+)*(?:class|record(?:[ \t]+class)?|struct|interface|enum)[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b";
        string[] baselineTypes = Regex.Matches(
                bootstrap.Content, typePattern, RegexOptions.CultureInvariant)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (baselineTypes.Length > 0 && !baselineTypes.Any(typeName => Regex.IsMatch(
                proposal,
                @"(?m)^[ \t]*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|readonly|ref|file)[ \t]+)*(?:class|record(?:[ \t]+class)?|struct|interface|enum)[ \t]+" +
                Regex.Escape(typeName) + @"\b",
                RegexOptions.CultureInvariant)))
        {
            return "The proposal is for the wrong C# type. The complete replacement for " +
                bootstrap.RelativePath + " must preserve at least one target declaration: " +
                string.Join(", ", baselineTypes) + ". Do not return a dependency type.";
        }

        return null;
    }

    private static string DescribeRejectedEdit(FileEditView edit)
    {
        List<string> lines =
        [
            $"Typed edit rejected: {edit.ErrorCode}: {edit.Error}",
        ];
        if (edit.CandidateCodeValidation is { } validation)
        {
            WorkbenchCodeValidationDiagnostic[] introduced = validation.Diagnostics
                .Where(item => item.Kind is WorkbenchCodeDiagnosticDeltaKind.Introduced)
                .Where(item => item.Diagnostic.Severity is
                    WorkbenchCodeDiagnosticSeverity.Warning or
                    WorkbenchCodeDiagnosticSeverity.Error)
                .Take(20)
                .ToArray();
            lines.AddRange(introduced
                .Select(item =>
                    $"{item.Diagnostic.Id.Value}: {item.Diagnostic.Message.Value} " +
                    $"at {item.Diagnostic.Path.Value}:" +
                    $"{item.Diagnostic.Range.Start.Line + 1}"));
            lines.AddRange(introduced
                .Select(item => DiagnosticRepairGuidance(item.Diagnostic.Id.Value))
                .Where(guidance => guidance is not null)
                .Distinct(StringComparer.Ordinal)
                .Select(guidance => "Deterministic repair guidance: " + guidance));
        }

        return string.Join('\n', lines);
    }

    private static string? DiagnosticRepairGuidance(string diagnosticId) => diagnosticId switch
    {
        "CS8600" =>
            "Fix the declaration at the cited line, not a different occurrence. The expression " +
            "may return null: change the receiving reference type from T to T? and retain an " +
            "explicit null check, or use a semantically correct non-null fallback. In particular, " +
            "every Console.ReadLine() result must be received by string? rather than string.",
        "CS8602" =>
            "Prove the receiver is non-null with an explicit guard before dereference, or use a " +
            "null-safe operation whose fallback preserves the required behavior.",
        "CS8603" =>
            "The return expression may be null. Return a non-null value on every path or make the " +
            "declared return type nullable when null is part of the contract.",
        "CS8618" =>
            "Initialize the non-nullable member in every constructor or mark it required/nullable " +
            "only when that matches the public contract.",
        "CS0122" =>
            "The referenced member is non-public. Remove every call to it. Construct values only " +
            "through public constructors or factories shown in dependency context, then derive " +
            "needed states through public transition methods. Tests must never synthesize state " +
            "through a private storage constructor or private helper.",
        "xUnit2017" =>
            "Replace Assert.True(collection.Contains(expected)) with the analyzer-requested " +
            "Assert.Contains(expected, collection) form at each cited line; preserve surrounding " +
            "test setup and assertions.",
        _ => null,
    };

    private sealed record BootstrapInspection(
        string Context,
        string DependencyContext,
        string RelativePath,
        string Sha256,
        string? Content);

}
