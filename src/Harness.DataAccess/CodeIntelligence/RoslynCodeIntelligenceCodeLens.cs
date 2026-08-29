using System.Collections.Immutable;
using Harness.DataAccess.Inspection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private static List<CodeIntelligenceCodeLens> BuildCodeLenses(
        SyntaxNode root,
        SourceText text,
        SemanticModel semanticModel,
        Project project,
        string rootPath,
        CodeIntelligenceInteractiveSnapshot snapshot,
        CodeIntelligenceCodeLensOptions options,
        CancellationToken cancellationToken)
    {
        List<CodeIntelligenceCodeLens> result = [];
        IMethodSymbol? entryPoint = semanticModel.Compilation.GetEntryPoint(cancellationToken);
        CodeIntelligenceExecutionTarget? executionTarget = EntryPointTarget(
            project, rootPath, entryPoint, snapshot);
        IEnumerable<SyntaxNode> declarations = root.DescendantNodes().Where(node =>
            node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or
                BaseMethodDeclarationSyntax or PropertyDeclarationSyntax or
                IndexerDeclarationSyntax or EventDeclarationSyntax);
        foreach (SyntaxNode declaration in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ISymbol? symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);
            if (symbol is null || symbol.Kind is SymbolKind.Local or SymbolKind.Parameter)
            {
                continue;
            }

            FileLinePositionSpan line = declaration.GetLocation().GetLineSpan();
            CodeIntelligencePosition position = new(line.StartLinePosition.Line, 0);
            CodeIntelligencePosition target = Position(text, DeclarationIdentifier(declaration));
            if (options.ShowReferences)
            {
                result.Add(new(position, target, CodeIntelligenceCodeLensKind.References,
                    new("Find references"), IsResolved: false));
            }
            if (options.ShowImplementations && CanHaveImplementations(symbol))
            {
                result.Add(new(position, target, CodeIntelligenceCodeLensKind.Implementations,
                    new("Find implementations"), IsResolved: false));
            }
            if (options.ShowTests && symbol is INamedTypeSymbol or IMethodSymbol)
            {
                result.Add(new(position, target, CodeIntelligenceCodeLensKind.Tests,
                    new("Find tests"), IsResolved: false));
            }
            if (executionTarget is not null && symbol is IMethodSymbol method &&
                SymbolEqualityComparer.Default.Equals(method, entryPoint))
            {
                if (options.ShowRun)
                {
                    result.Add(new(position, target, CodeIntelligenceCodeLensKind.Run,
                        new("Run project"), IsResolved: true, executionTarget));
                }
                if (options.ShowDebug)
                {
                    result.Add(new(position, target, CodeIntelligenceCodeLensKind.Debug,
                        new("Debug project"), IsResolved: true, executionTarget));
                }
            }
        }

        return result
            .DistinctBy(item => (item.Position, item.Target, item.Kind))
            .OrderBy(item => item.Position.Line)
            .ThenBy(item => item.Kind)
            .ToList();
    }

    private static CodeIntelligenceExecutionTarget? EntryPointTarget(
        Project project,
        string rootPath,
        IMethodSymbol? entryPoint,
        CodeIntelligenceInteractiveSnapshot snapshot)
    {
        if (entryPoint is null || string.IsNullOrWhiteSpace(project.FilePath))
        {
            return null;
        }
        string relative = Path.GetRelativePath(rootPath, project.FilePath);
        if (!WorkspacePathPolicy.TryResolve(
                rootPath, relative, out _, out string confinedProject, out _,
                out _, out _))
        {
            return null;
        }

        AnalyzerConfigOptions options =
            project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        _ = options.TryGetValue("build_property.TargetFramework", out string? framework);
        string declaration = entryPoint.GetDocumentationCommentId() ??
                             entryPoint.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new(
            CodeIntelligenceExecutionTargetKind.ProjectEntryPoint,
            new(confinedProject.Replace(Path.DirectorySeparatorChar, '/')),
            new(string.IsNullOrWhiteSpace(framework) ? "unknown" : framework),
            new(Bound(declaration, MaximumIssueLength)),
            snapshot.Path,
            snapshot.BaselineHash,
            snapshot.BufferVersion);
    }

    private static bool IsObviousArgument(ExpressionSyntax expression, string parameterName)
    {
        string? expressionName = expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => null,
        };
        return expressionName is not null && NormalizeName(expressionName).Equals(
            NormalizeName(parameterName), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value) => value.TrimStart('_');

    private static bool CanHaveImplementations(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol => true,
        IMethodSymbol method => method.IsAbstract || method.IsVirtual || method.IsOverride ||
                                method.ContainingType.TypeKind is TypeKind.Interface,
        IPropertySymbol property => property.IsAbstract || property.IsVirtual ||
                                    property.IsOverride ||
                                    property.ContainingType.TypeKind is TypeKind.Interface,
        IEventSymbol @event => @event.IsAbstract || @event.IsVirtual || @event.IsOverride ||
                               @event.ContainingType.TypeKind is TypeKind.Interface,
        _ => false,
    };

    private static int DeclarationIdentifier(SyntaxNode declaration) => declaration switch
    {
        BaseTypeDeclarationSyntax value => value.Identifier.SpanStart,
        DelegateDeclarationSyntax value => value.Identifier.SpanStart,
        MethodDeclarationSyntax value => value.Identifier.SpanStart,
        ConstructorDeclarationSyntax value => value.Identifier.SpanStart,
        DestructorDeclarationSyntax value => value.Identifier.SpanStart,
        OperatorDeclarationSyntax value => value.OperatorToken.SpanStart,
        ConversionOperatorDeclarationSyntax value => value.Type.SpanStart,
        PropertyDeclarationSyntax value => value.Identifier.SpanStart,
        IndexerDeclarationSyntax value => value.ThisKeyword.SpanStart,
        EventDeclarationSyntax value => value.Identifier.SpanStart,
        _ => declaration.SpanStart,
    };

    private static bool IsWriteReference(
        ReferenceLocation reference,
        SyntaxNode? root)
    {
        SyntaxNode? node = root?.FindNode(reference.Location.SourceSpan,
            getInnermostNodeForTie: true);
        SyntaxNode? expression = node?.AncestorsAndSelf().FirstOrDefault(item =>
            item.Span == reference.Location.SourceSpan || item is ExpressionSyntax);
        SyntaxNode? parent = expression?.Parent;
        return parent switch
        {
            AssignmentExpressionSyntax assignment when assignment.Left.Span.Contains(
                reference.Location.SourceSpan) => true,
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(
                SyntaxKind.PreIncrementExpression) || prefix.IsKind(
                SyntaxKind.PreDecrementExpression) => true,
            PostfixUnaryExpressionSyntax postfix when postfix.IsKind(
                SyntaxKind.PostIncrementExpression) || postfix.IsKind(
                SyntaxKind.PostDecrementExpression) => true,
            ArgumentSyntax argument when argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword) ||
                                         argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) => true,
            _ => false,
        };
    }

}
