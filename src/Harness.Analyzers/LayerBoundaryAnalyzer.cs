using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Harness.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayerBoundaryAnalyzer : DiagnosticAnalyzer
{
    public const string InvalidLayerUsageId = "HARNESS001";
    public const string InvalidBoundaryTypeId = "HARNESS002";

    private static readonly DiagnosticDescriptor InvalidLayerUsage = new(
        InvalidLayerUsageId,
        "Layer dependency points in the wrong direction",
        "Layer '{0}' cannot use symbols from '{1}'",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidBoundaryType = new(
        InvalidBoundaryTypeId,
        "Public layer contract must be an interface, record, or enum",
        "Public type '{0}' must be an interface, record, or enum to cross layer boundaries",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [InvalidLayerUsage, InvalidBoundaryType];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startContext =>
        {
            Layer currentLayer = GetLayer(startContext.Compilation.AssemblyName);
            if (currentLayer is Layer.Other)
            {
                return;
            }

            startContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeNameUsage(syntaxContext, currentLayer),
                SyntaxKind.IdentifierName,
                SyntaxKind.GenericName);

            if (currentLayer is not Layer.Host)
            {
                startContext.RegisterSymbolAction(AnalyzePublicType, SymbolKind.NamedType);
            }
        });
    }

    private static void AnalyzeNameUsage(SyntaxNodeAnalysisContext context, Layer currentLayer)
    {
        NameSyntax name = (NameSyntax)context.Node;
        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
        string? referencedAssembly = symbol?.ContainingAssembly?.Name;
        Layer referencedLayer = GetLayer(referencedAssembly);

        if (referencedLayer is Layer.Other || IsAllowed(currentLayer, referencedLayer))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            InvalidLayerUsage,
            name.GetLocation(),
            context.Compilation.AssemblyName,
            referencedAssembly));
    }

    private static void AnalyzePublicType(SymbolAnalysisContext context)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
        if (!IsEffectivelyPublic(type) ||
            type.TypeKind is TypeKind.Interface or TypeKind.Enum ||
            type.IsRecord)
        {
            return;
        }

        Location? location = type.Locations.FirstOrDefault(static location => location.IsInSource);
        if (location is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidBoundaryType, location, type.Name));
        }
    }

    private static bool IsEffectivelyPublic(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllowed(Layer current, Layer referenced) => current switch
    {
        Layer.DataAccess => referenced is Layer.DataAccess,
        Layer.BusinessLogic => referenced is Layer.DataAccess or Layer.BusinessLogic,
        Layer.Presentation => referenced is Layer.BusinessLogic or Layer.Presentation,
        Layer.Host => true,
        _ => true,
    };

    private static Layer GetLayer(string? assemblyName) => assemblyName switch
    {
        "Harness.DataAccess" => Layer.DataAccess,
        "Harness.BusinessLogic" => Layer.BusinessLogic,
        string name when name.StartsWith("Harness.Presentation.", StringComparison.Ordinal) => Layer.Presentation,
        "Harness.Host" => Layer.Host,
        _ => Layer.Other,
    };

    private enum Layer
    {
        Other,
        DataAccess,
        BusinessLogic,
        Presentation,
        Host,
    }
}
