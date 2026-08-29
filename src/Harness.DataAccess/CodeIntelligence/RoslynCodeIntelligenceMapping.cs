using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private static CodeIntelligenceResultState SessionState(ActiveSession session) =>
        session.Issues.IsEmpty
            ? CodeIntelligenceResultState.Ready
            : CodeIntelligenceResultState.Degraded;

    private static CodeIntelligenceRange Range(SourceText text, TextSpan span)
    {
        LinePositionSpan lines = text.Lines.GetLinePositionSpan(span);
        return new(
            new(lines.Start.Line, lines.Start.Character),
            new(lines.End.Line, lines.End.Character));
    }

    private static CodeIntelligencePosition Position(SourceText text, int offset)
    {
        LinePosition position = text.Lines.GetLinePosition(Math.Clamp(offset, 0, text.Length));
        return new(position.Line, position.Character);
    }

    private static CodeIntelligenceSymbolKind MapSymbolKind(ImmutableArray<string> tags)
    {
        if (tags.Contains("Keyword")) return CodeIntelligenceSymbolKind.Keyword;
        if (tags.Contains("Namespace")) return CodeIntelligenceSymbolKind.Namespace;
        if (tags.Contains("Class")) return CodeIntelligenceSymbolKind.Class;
        if (tags.Contains("Interface")) return CodeIntelligenceSymbolKind.Interface;
        if (tags.Contains("Structure")) return CodeIntelligenceSymbolKind.Structure;
        if (tags.Contains("Enum")) return CodeIntelligenceSymbolKind.Enumeration;
        if (tags.Contains("Delegate")) return CodeIntelligenceSymbolKind.Delegate;
        if (tags.Contains("ExtensionMethod")) return CodeIntelligenceSymbolKind.ExtensionMethod;
        if (tags.Contains("Method")) return CodeIntelligenceSymbolKind.Method;
        if (tags.Contains("Property")) return CodeIntelligenceSymbolKind.Property;
        if (tags.Contains("Field")) return CodeIntelligenceSymbolKind.Field;
        if (tags.Contains("Event")) return CodeIntelligenceSymbolKind.Event;
        if (tags.Contains("Constant")) return CodeIntelligenceSymbolKind.Constant;
        if (tags.Contains("Local")) return CodeIntelligenceSymbolKind.Local;
        if (tags.Contains("Parameter")) return CodeIntelligenceSymbolKind.Parameter;
        if (tags.Contains("TypeParameter")) return CodeIntelligenceSymbolKind.TypeParameter;
        if (tags.Contains("Snippet")) return CodeIntelligenceSymbolKind.Snippet;
        return CodeIntelligenceSymbolKind.Other;
    }

    private static IReadOnlyList<char> CommitCharacters(CompletionItemRules rules)
    {
        HashSet<char> characters =
        [
            ' ', '(', ')', '[', ']', '{', '}', ':', ';', ',', '.', '+', '-', '*', '/', '%',
            '&', '|', '^', '!', '~', '=', '<', '>', '?', '@', '#', '\'', '"', '\\',
        ];
        foreach (CharacterSetModificationRule rule in rules.CommitCharacterRules)
        {
            switch (rule.Kind)
            {
                case CharacterSetModificationKind.Add:
                    characters.UnionWith(rule.Characters);
                    break;
                case CharacterSetModificationKind.Remove:
                    characters.ExceptWith(rule.Characters);
                    break;
                case CharacterSetModificationKind.Replace:
                    characters.Clear();
                    characters.UnionWith(rule.Characters);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rules));
            }
        }

        return characters.Order().ToArray();
    }

    private static CodeIntelligenceSignatureItem MapSignature(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        SignatureDocumentation documentation = Documentation(method, cancellationToken);
        return new(
            new(Bound(
                method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                MaximumIssueLength)),
            new(documentation.Summary),
            method.Parameters.Select(parameter => new CodeIntelligenceSignatureParameter(
                new(parameter.Name),
                new(Bound(parameter.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    MaximumIssueLength)),
                new(documentation.Parameters.GetValueOrDefault(parameter.Name, string.Empty))))
                .ToArray());
    }

    private static SignatureDocumentation Documentation(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        string? xml = method.GetDocumentationCommentXml(
            expandIncludes: false,
            cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return new(string.Empty, new Dictionary<string, string>(StringComparer.Ordinal));
        }

        try
        {
            XDocument document = XDocument.Parse(xml, LoadOptions.None);
            string summary = NormalizeDocumentation(document.Root?.Element("summary")?.Value);
            Dictionary<string, string> parameters = document.Root?.Elements("param")
                .Where(element => element.Attribute("name")?.Value is { Length: > 0 })
                .GroupBy(element => element.Attribute("name")!.Value, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => NormalizeDocumentation(group.First().Value),
                    StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
            return new(summary, parameters);
        }
        catch (XmlException)
        {
            return new(string.Empty, new Dictionary<string, string>(StringComparer.Ordinal));
        }
    }

    private static string NormalizeDocumentation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Bound(string.Join(' ', value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            MaximumIssueLength);
    }

    private static CodeIntelligenceSymbolDestination MapDestination(
        Location location,
        string display,
        string root)
    {
        if (!location.IsInSource || location.SourceTree?.FilePath is not { } sourcePath)
        {
            return new(
                CodeIntelligenceDestinationKind.Metadata,
                new(Bound(display, MaximumIssueLength)),
                null,
                null);
        }

        string fullPath = Path.GetFullPath(sourcePath);
        string relative = Path.GetRelativePath(root, fullPath);
        bool confined = relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        FileLinePositionSpan span = location.GetLineSpan();
        return new(
            confined && File.Exists(fullPath)
                ? CodeIntelligenceDestinationKind.Source
                : CodeIntelligenceDestinationKind.Generated,
            new(Bound(display, MaximumIssueLength)),
            confined ? new(relative) : null,
            new(
                new(span.StartLinePosition.Line, span.StartLinePosition.Character),
                new(span.EndLinePosition.Line, span.EndLinePosition.Character)));
    }

    private static CodeIntelligenceSymbolDestination UnavailableDestination(string message) => new(
        CodeIntelligenceDestinationKind.Unavailable,
        new(message),
        null,
        null);

}
