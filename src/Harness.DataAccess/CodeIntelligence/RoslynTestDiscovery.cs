using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private const int MaximumDiscoveredTests = 10_000;
    private const int MaximumTestTraits = 32;
    private const int MaximumTestTextLength = 512;

    public async ValueTask<CodeIntelligenceTestDiscoveryResult> DiscoverTestsAsync(
        CodeIntelligenceTestDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaximumResults is < 1 or > 2_000 || request.Offset < 0 ||
            request.Offset > MaximumDiscoveredTests || request.Query?.Length > 256 ||
            request.Framework is { } framework && !Enum.IsDefined(framework))
        {
            return TestDiscoveryFailure(request, "invalid_test_query",
                "The test query, result limit, or continuation is outside the bounded range.");
        }

        ActiveSession? session = activeSession;
        if (session is null || session.ContextId != request.ContextId ||
            session.SessionId != request.SessionId)
        {
            return TestDiscoveryFailure(request, "session_unavailable",
                "The Roslyn session no longer matches this source context.",
                CodeIntelligenceResultState.Stale);
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            List<CodeIntelligenceTestCase> discovered = [];
            bool bounded = false;
            foreach (Project project in session.CurrentSolution.Projects
                         .OrderBy(item => item.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryProjectPath(session.RootPath, project.FilePath, out string projectPath))
                    continue;
                foreach (Document document in project.Documents
                             .OrderBy(item => item.FilePath, StringComparer.Ordinal))
                {
                    SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
                    SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken);
                    if (root is null || model is null) continue;
                    foreach (MethodDeclarationSyntax declaration in root.DescendantNodes()
                                 .OfType<MethodDeclarationSyntax>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (model.GetDeclaredSymbol(declaration, cancellationToken) is not
                            IMethodSymbol method || !TryClassifyTest(method, out TestClassification test))
                            continue;
                        if (request.Framework is { } selectedFramework &&
                            test.Framework != selectedFramework)
                            continue;
                        Location location = method.Locations.FirstOrDefault(item => item.IsInSource)
                            ?? Location.None;
                        CodeIntelligenceSymbolDestination destination = MapDestination(
                            location, method.Name, session.RootPath);
                        if (destination.Path is null || destination.Range is null) continue;
                        string fullyQualifiedName = FullyQualifiedName(method);
                        IReadOnlyList<CodeIntelligenceTestTrait> traits = Traits(method);
                        if (!MatchesTestQuery(
                                request.Query,
                                projectPath,
                                fullyQualifiedName,
                                test.DisplayName ?? method.Name,
                                traits))
                            continue;
                        if (discovered.Count >= MaximumDiscoveredTests)
                        {
                            bounded = true;
                            break;
                        }
                        discovered.Add(new(
                            new(Hash(projectPath + "\n" +
                                (method.GetDocumentationCommentId() ?? fullyQualifiedName))),
                            new(projectPath),
                            test.Framework,
                            new(Bound(fullyQualifiedName, MaximumTestTextLength)),
                            new(Bound(test.DisplayName ?? method.Name, MaximumTestTextLength)),
                            destination.Path,
                            destination.Range,
                            traits,
                            test.IsParameterized));
                    }
                    if (bounded) break;
                }
                if (bounded) break;
            }

            CodeIntelligenceTestCase[] ordered = discovered
                .OrderBy(item => item.ProjectPath.Value, StringComparer.Ordinal)
                .ThenBy(item => item.FullyQualifiedName.Value, StringComparer.Ordinal)
                .ToArray();
            CodeIntelligenceTestCase[] page = ordered.Skip(request.Offset)
                .Take(request.MaximumResults).ToArray();
            bool truncated = bounded || ordered.Length > request.Offset + page.Length;
            int nextOffset = request.Offset + page.Length;
            return new(
                request.ContextId,
                request.SessionId,
                SessionState(session),
                page,
                truncated && page.Length > 0 && nextOffset < MaximumDiscoveredTests
                    ? nextOffset
                    : null,
                truncated,
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException)
        {
            return TestDiscoveryFailure(request, "test_discovery_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    private static bool TryClassifyTest(
        IMethodSymbol method,
        out TestClassification classification)
    {
        TestClassification? discovered = null;
        foreach (AttributeData attribute in method.GetAttributes())
        {
            for (INamedTypeSymbol? type = attribute.AttributeClass;
                 type is not null;
                 type = type.BaseType)
            {
                string name = type.ToDisplayString();
                (CodeIntelligenceTestFramework Framework, bool Parameterized)? match = name switch
                {
                    "Xunit.FactAttribute" => (CodeIntelligenceTestFramework.XUnit, false),
                    "Xunit.TheoryAttribute" => (CodeIntelligenceTestFramework.XUnit, true),
                    "NUnit.Framework.TestAttribute" => (CodeIntelligenceTestFramework.NUnit, false),
                    "NUnit.Framework.TestCaseAttribute" or
                    "NUnit.Framework.TestCaseSourceAttribute" or
                    "NUnit.Framework.TheoryAttribute" =>
                        (CodeIntelligenceTestFramework.NUnit, true),
                    "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" =>
                        (CodeIntelligenceTestFramework.MSTest, false),
                    "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute" =>
                        (CodeIntelligenceTestFramework.MSTest, true),
                    _ => null,
                };
                if (match is null || discovered is { } existing &&
                    existing.Framework != match.Value.Framework)
                    continue;
                discovered = new(
                    match.Value.Framework,
                    match.Value.Parameterized || discovered?.IsParameterized is true,
                    discovered?.DisplayName ?? AttributeDisplayName(attribute));
            }
        }
        classification = discovered ?? default;
        return discovered is not null;
    }

    private static string? AttributeDisplayName(AttributeData attribute)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key is not ("DisplayName" or "TestName") ||
                argument.Value.Value is not string value || string.IsNullOrWhiteSpace(value))
                continue;
            return Bound(value, MaximumTestTextLength);
        }
        return null;
    }

    private static IReadOnlyList<CodeIntelligenceTestTrait> Traits(IMethodSymbol method) =>
        method.ContainingType.GetAttributes().Concat(method.GetAttributes())
            .SelectMany(Traits)
            .DistinctBy(item => (item.Name.Value, item.Value.Value))
            .Take(MaximumTestTraits)
            .ToArray();

    private static IEnumerable<CodeIntelligenceTestTrait> Traits(AttributeData attribute)
    {
        string name = attribute.AttributeClass?.ToDisplayString() ?? string.Empty;
        if (name is "Xunit.TraitAttribute" or "NUnit.Framework.PropertyAttribute" or
            "Microsoft.VisualStudio.TestTools.UnitTesting.TestPropertyAttribute")
        {
            string? traitName = Scalar(attribute.ConstructorArguments.ElementAtOrDefault(0));
            string? traitValue = Scalar(attribute.ConstructorArguments.ElementAtOrDefault(1));
            if (traitName is not null && traitValue is not null)
                yield return Trait(traitName, traitValue);
        }
        else if (name is "NUnit.Framework.CategoryAttribute" or
                 "Microsoft.VisualStudio.TestTools.UnitTesting.TestCategoryAttribute")
        {
            foreach (string value in Values(attribute.ConstructorArguments))
                yield return Trait("Category", value);
        }
    }

    private static IEnumerable<string> Values(IEnumerable<TypedConstant> arguments)
    {
        foreach (TypedConstant argument in arguments)
        {
            if (argument.Kind is TypedConstantKind.Array)
            {
                foreach (TypedConstant item in argument.Values)
                    if (Scalar(item) is { } value) yield return value;
            }
            else if (Scalar(argument) is { } value)
                yield return value;
        }
    }

    private static string? Scalar(TypedConstant value) => value.Value switch
    {
        string text when !string.IsNullOrWhiteSpace(text) =>
            Bound(text, MaximumTestTextLength),
        _ => null,
    };

    private static CodeIntelligenceTestTrait Trait(string name, string value) => new(
        new(Bound(name, MaximumTestTextLength)),
        new(Bound(value, MaximumTestTextLength)));

    private static string FullyQualifiedName(IMethodSymbol method)
    {
        string type = method.ContainingType.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);
        return type + "." + method.Name;
    }

    private static bool TryProjectPath(string root, string? path, out string relative)
    {
        relative = string.Empty;
        if (path is null) return false;
        string full = Path.GetFullPath(path);
        string candidate = Path.GetRelativePath(root, full);
        if (candidate == ".." ||
            candidate.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            return false;
        relative = candidate.Replace(Path.DirectorySeparatorChar, '/');
        return true;
    }

    private static bool MatchesTestQuery(
        string? query,
        string project,
        string fullyQualifiedName,
        string displayName,
        IReadOnlyList<CodeIntelligenceTestTrait> traits)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return project.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               fullyQualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               traits.Any(item => item.Name.Value.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   item.Value.Value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static CodeIntelligenceTestDiscoveryResult TestDiscoveryFailure(
        CodeIntelligenceTestDiscoveryRequest request,
        string code,
        string message,
        CodeIntelligenceResultState state = CodeIntelligenceResultState.Failed) => new(
        request.ContextId,
        request.SessionId,
        state,
        [],
        Continuation: null,
        IsTruncated: false,
        [Issue(code, message)]);

    private readonly record struct TestClassification(
        CodeIntelligenceTestFramework Framework,
        bool IsParameterized,
        string? DisplayName);
}
