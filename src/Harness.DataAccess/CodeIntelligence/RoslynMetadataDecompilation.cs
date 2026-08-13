using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private const string MetadataDecompilerVersion = "10.1.1.8388";
    private const int MaximumMetadataAssemblyCandidates = 16;
    private const int MaximumMetadataResolverAssemblies = 2_048;
    private const long MaximumMetadataAssemblyBytes = 256L * 1024 * 1024;

    private static async ValueTask<MetadataDecompilation?> TryDecompileMetadataAsync(
        Project project,
        ISymbol selected,
        CodeIntelligenceVirtualDocumentOrigin origin,
        CancellationToken cancellationToken)
    {
        INamedTypeSymbol? type = (selected as INamedTypeSymbol ?? selected.ContainingType)?
            .OriginalDefinition;
        Compilation? compilation = type is null
            ? null
            : await project.GetCompilationAsync(cancellationToken);
        if (type is null || compilation is null || type.ContainingAssembly is null)
            return null;

        string metadataTypeName = MetadataTypeName(type);
        string expectedAssembly = type.ContainingAssembly.Identity.GetDisplayName();
        string[] localAssemblies = LocalAssemblyPaths(compilation)
            .Take(MaximumMetadataResolverAssemblies).ToArray();
        foreach (string assemblyPath in localAssemblies.Where(path =>
                     Path.GetFileNameWithoutExtension(path).Equals(
                         type.ContainingAssembly.Identity.Name,
                         StringComparison.OrdinalIgnoreCase) &&
                     AssemblyIdentityMatches(path, expectedAssembly))
                     .Take(MaximumMetadataAssemblyCandidates))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (new FileInfo(assemblyPath).Length > MaximumMetadataAssemblyBytes) continue;
                using FileStream stream = new(assemblyPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
                using PEReader reader = new(stream, PEStreamOptions.PrefetchMetadata);
                if (!reader.HasMetadata) continue;
                MetadataReader metadata = reader.GetMetadataReader();
                if (!metadata.IsAssembly) continue;
                AssemblyDefinition assembly = metadata.GetAssemblyDefinition();
                if (!AssemblyIdentityMatches(assemblyPath,
                        type.ContainingAssembly.Identity.GetDisplayName()))
                    continue;
                if (IsReferenceAssembly(metadata, assembly)) continue;
                TypeDefinitionHandle typeHandle = metadata.TypeDefinitions.FirstOrDefault(candidate =>
                    MetadataTypeName(metadata, candidate).Equals(metadataTypeName,
                        StringComparison.Ordinal));
                if (typeHandle.IsNil) continue;
                MetadataEntitySelection entity = SelectMetadataEntity(
                    metadata, typeHandle, selected.OriginalDefinition);
                if (entity.Handles.Count == 0 || !entity.HasImplementation) continue;

                DecompilerSettings settings = new()
                {
                    ThrowOnAssemblyResolveErrors = false,
                };
                using CompilationAssemblyResolver resolver = new(localAssemblies);
                CSharpDecompiler decompiler = new(assemblyPath, resolver, settings)
                {
                    CancellationToken = cancellationToken,
                };
                string source = entity.IsWholeType
                    ? decompiler.DecompileTypesAsString([typeHandle])
                    : decompiler.DecompileAsString(entity.Handles);
                string header = "// Decompiled locally by Harness.NET with " +
                                $"ICSharpCode.Decompiler {MetadataDecompilerVersion}.\n" +
                                $"// Assembly: {origin.Assembly.Value}\n" +
                                $"// Project: {origin.Project.Value} · " +
                                $"{origin.TargetFramework.Value} · {origin.Configuration.Value}\n" +
                                "// Read-only reconstruction; it may differ from the original source.\n\n";
                string content = header + source.TrimEnd() + "\n";
                if (content.Length > MaximumVirtualDocumentCharacters) continue;
                return new(content);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (IsRecoverableMetadataFailure(exception))
            {
                // A broken or unresolved local image degrades to the signature renderer.
            }
        }
        return null;
    }

    private static IEnumerable<string> LocalAssemblyPaths(Compilation compilation)
    {
        HashSet<string> yielded = new(StringComparer.Ordinal);
        string? trustedPlatformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            {
                if (File.Exists(path) && yielded.Add(path))
                    yield return path;
            }
        }

        foreach (PortableExecutableReference reference in compilation.References
                     .OfType<PortableExecutableReference>())
        {
            if (reference.FilePath is not { } path || !Path.IsPathFullyQualified(path) ||
                !File.Exists(path))
                continue;
            if (yielded.Add(path)) yield return path;
        }
    }

    private static bool IsRecoverableMetadataFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or BadImageFormatException or
        InvalidOperationException or ArgumentException or NotSupportedException or
        ResolutionException;

    private static bool IsReferenceAssembly(
        MetadataReader metadata,
        AssemblyDefinition assembly) => assembly.GetCustomAttributes().Any(handle =>
    {
        CustomAttribute attribute = metadata.GetCustomAttribute(handle);
        if (attribute.Constructor.Kind is not HandleKind.MemberReference) return false;
        MemberReference constructor = metadata.GetMemberReference(
            (MemberReferenceHandle)attribute.Constructor);
        return constructor.Parent.Kind is HandleKind.TypeReference &&
               MetadataTypeName(metadata, (TypeReferenceHandle)constructor.Parent).Equals(
                   "System.Runtime.CompilerServices.ReferenceAssemblyAttribute",
                   StringComparison.Ordinal);
    });

    private static bool AssemblyIdentityMatches(string path, string expected)
    {
        try
        {
            string? actual = AssemblyName.GetAssemblyName(path).FullName;
            return actual is not null && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (IsRecoverableMetadataFailure(exception))
        {
            return false;
        }
    }

    private static MetadataEntitySelection SelectMetadataEntity(
        MetadataReader metadata,
        TypeDefinitionHandle typeHandle,
        ISymbol selected)
    {
        TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
        switch (selected)
        {
            case IMethodSymbol method:
                {
                    MetadataTypeNameProvider names = new(metadata);
                    MethodDefinitionHandle[] methods = type.GetMethods().Where(handle =>
                    {
                        MethodDefinition candidate = metadata.GetMethodDefinition(handle);
                        if (metadata.GetString(candidate.Name) != method.MetadataName) return false;
                        MethodSignature<string> signature = candidate.DecodeSignature(
                            names, genericContext: null);
                        return signature.GenericParameterCount == method.Arity &&
                               signature.ParameterTypes.Length == method.Parameters.Length &&
                               signature.ParameterTypes.Select((parameter, index) =>
                                   parameter == SymbolMetadataTypeName(method.Parameters[index]))
                                   .All(matches => matches);
                    }).ToArray();
                    bool hasBody = method.IsAbstract || method.IsExtern || methods.Any(handle =>
                        metadata.GetMethodDefinition(handle).RelativeVirtualAddress != 0);
                    return new(methods.Select(handle => (EntityHandle)handle).ToArray(),
                        IsWholeType: false, hasBody);
                }
            case IPropertySymbol property:
                {
                    PropertyDefinitionHandle[] properties = type.GetProperties().Where(handle =>
                        metadata.GetString(metadata.GetPropertyDefinition(handle).Name) ==
                        property.MetadataName).ToArray();
                    bool hasBody = property.IsAbstract || properties.Any(handle =>
                    {
                        PropertyAccessors accessors = metadata.GetPropertyDefinition(handle).GetAccessors();
                        return (!accessors.Getter.IsNil && metadata.GetMethodDefinition(
                                    accessors.Getter).RelativeVirtualAddress != 0) ||
                               (!accessors.Setter.IsNil && metadata.GetMethodDefinition(
                                    accessors.Setter).RelativeVirtualAddress != 0);
                    });
                    return new(properties.Select(handle => (EntityHandle)handle).ToArray(),
                        IsWholeType: false, hasBody);
                }
            case IFieldSymbol field:
                return new(type.GetFields().Where(handle => metadata.GetString(
                            metadata.GetFieldDefinition(handle).Name) == field.MetadataName)
                        .Select(handle => (EntityHandle)handle).ToArray(), IsWholeType: false,
                    HasImplementation: true);
            case IEventSymbol @event:
                return new(type.GetEvents().Where(handle => metadata.GetString(
                            metadata.GetEventDefinition(handle).Name) == @event.MetadataName)
                        .Select(handle => (EntityHandle)handle).ToArray(), IsWholeType: false,
                    HasImplementation: true);
            default:
                return new([typeHandle], IsWholeType: true,
                    type.GetMethods().Any(handle => metadata.GetMethodDefinition(
                        handle).RelativeVirtualAddress != 0));
        }
    }

    private sealed record MetadataDecompilation(string Text);
    private sealed record MetadataEntitySelection(
        IReadOnlyList<EntityHandle> Handles,
        bool IsWholeType,
        bool HasImplementation);

    private sealed class CompilationAssemblyResolver : IAssemblyResolver, IDisposable
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> paths;
        private readonly Dictionary<string, MetadataFile> resolved =
            new(StringComparer.OrdinalIgnoreCase);

        public CompilationAssemblyResolver(IEnumerable<string> candidates)
        {
            Dictionary<string, List<string>> byName = new(StringComparer.OrdinalIgnoreCase);
            foreach (string path in candidates)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (!byName.TryGetValue(name, out List<string>? matches))
                    byName.Add(name, matches = []);
                matches.Add(path);
            }
            paths = byName.ToDictionary(
                item => item.Key,
                item => (IReadOnlyList<string>)item.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        public MetadataFile? Resolve(IAssemblyReference reference)
        {
            if (resolved.TryGetValue(reference.FullName, out MetadataFile? existing))
                return existing;
            if (!paths.TryGetValue(reference.Name, out IReadOnlyList<string>? candidates))
                return null;
            string? path = candidates.FirstOrDefault(candidate =>
                new FileInfo(candidate).Length <= MaximumMetadataAssemblyBytes &&
                AssemblyIdentityMatches(candidate, reference.FullName));
            if (path is null) return null;
            PEFile file = new(path, PEStreamOptions.PrefetchEntireImage);
            resolved.Add(reference.FullName, file);
            return file;
        }

        public MetadataFile? ResolveModule(MetadataFile mainModule, string moduleName) => null;

        public Task<MetadataFile?> ResolveAsync(IAssemblyReference reference) =>
            Task.FromResult(Resolve(reference));

        public Task<MetadataFile?> ResolveModuleAsync(
            MetadataFile mainModule,
            string moduleName) => Task.FromResult<MetadataFile?>(null);

        public void Dispose()
        {
            foreach (MetadataFile file in resolved.Values) file.Dispose();
            resolved.Clear();
        }
    }
}
