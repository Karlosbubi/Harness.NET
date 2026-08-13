using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private const int MaximumInspectionCharacters = 2 * 1024 * 1024;
    private const int MaximumSyntaxItems = 4_000;
    private const int MaximumGeneratedInspectionDocuments = 20;
    private const int MaximumIlBytesPerMethod = 64 * 1024;
    private static readonly IReadOnlyDictionary<ushort, OpCode> IlOpCodes = BuildOpCodes();

    public async ValueTask<CodeIntelligenceInspectionResult> InspectAsync(
        CodeIntelligenceInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ActiveSession? session = MatchingSession(request.Snapshot);
        if (session is null || !Enum.IsDefined(request.Kind))
            return InspectionFailure(request, CodeIntelligenceResultState.Stale,
                "inspection_session_unavailable",
                "The inspection request does not match an active Roslyn session.");

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, request.Snapshot, cancellationToken);
            if (prepared.Issue is not null)
                return InspectionFailure(request, prepared.State, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value);
            Project project = prepared.Document!.Project;
            CodeIntelligenceVirtualDocumentOrigin? origin = await OriginAsync(
                project, request.Snapshot, cancellationToken);
            if (origin is null)
                return InspectionFailure(request, CodeIntelligenceResultState.Degraded,
                    "inspection_compilation_unavailable",
                    "The project did not produce an exact compilation identity.");

            return request.Kind switch
            {
                CodeIntelligenceInspectionKind.SyntaxTree => await SyntaxTreeInspectionAsync(
                    request, prepared, origin, cancellationToken),
                CodeIntelligenceInspectionKind.Symbol => await SymbolInspectionAsync(
                    request, prepared, origin, cancellationToken),
                CodeIntelligenceInspectionKind.GeneratedSource => await GeneratedInspectionAsync(
                    request, project, origin, cancellationToken),
                CodeIntelligenceInspectionKind.IntermediateLanguage => await IlInspectionAsync(
                    request, prepared, origin, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(request.Kind)),
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is InvalidOperationException or
                                          ArgumentException or NotSupportedException or
                                          BadImageFormatException)
        {
            return InspectionFailure(request, CodeIntelligenceResultState.Failed,
                "inspection_failed", exception.Message);
        }
        finally { session.OperationGate.Release(); }
    }

    private static async ValueTask<CodeIntelligenceInspectionResult> SyntaxTreeInspectionAsync(
        CodeIntelligenceInspectionRequest request,
        PreparedInteractive prepared,
        CodeIntelligenceVirtualDocumentOrigin origin,
        CancellationToken cancellationToken)
    {
        SyntaxNode? root = await prepared.Document!.GetSyntaxRootAsync(cancellationToken);
        if (root is null)
            return InspectionFailure(request, CodeIntelligenceResultState.Degraded,
                "syntax_tree_unavailable", "Roslyn did not produce a syntax tree.");
        SyntaxNode focus = root.FindToken(prepared.Offset).Parent?.AncestorsAndSelf()
            .FirstOrDefault(IsInspectionScope) ?? root;
        StringBuilder text = Header("Syntax tree", request, origin);
        text.AppendLine($"Scope: {focus.Kind()} [{focus.Span.Start}..{focus.Span.End})");
        text.AppendLine();
        int count = 0;
        bool truncated = false;
        Append(focus, depth: 0);
        return InspectionSuccess(request, "Syntax tree", text, origin, truncated);

        void Append(SyntaxNodeOrToken item, int depth)
        {
            if (truncated || ++count > MaximumSyntaxItems ||
                text.Length >= MaximumInspectionCharacters)
            {
                truncated = true;
                return;
            }
            text.Append(' ', Math.Min(depth, 40) * 2);
            text.Append(item.Kind()).Append(" [")
                .Append(item.Span.Start).Append("..").Append(item.Span.End).Append(')');
            if (item.IsToken)
            {
                SyntaxToken token = item.AsToken();
                text.Append(" token=\"").Append(OneLine(token.Text, 120)).Append('"');
                if (token.IsMissing) text.Append(" missing");
            }
            text.AppendLine();
            if (item.IsNode)
                foreach (SyntaxNodeOrToken child in item.AsNode()!.ChildNodesAndTokens())
                    Append(child, depth + 1);
        }
    }

    private static async ValueTask<CodeIntelligenceInspectionResult> SymbolInspectionAsync(
        CodeIntelligenceInspectionRequest request,
        PreparedInteractive prepared,
        CodeIntelligenceVirtualDocumentOrigin origin,
        CancellationToken cancellationToken)
    {
        SemanticModel? model = await prepared.Document!.GetSemanticModelAsync(cancellationToken);
        SyntaxNode? root = await prepared.Document.GetSyntaxRootAsync(cancellationToken);
        ISymbol? symbol = model is null || root is null
            ? null : ResolveInspectionSymbol(model, root, prepared.Offset, cancellationToken);
        if (symbol is null)
            return InspectionFailure(request, CodeIntelligenceResultState.Degraded,
                "symbol_unavailable", "No semantic symbol exists at the current caret.");

        StringBuilder text = Header("Symbol details", request, origin);
        text.AppendLine($"Display: {symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
        text.AppendLine($"Kind: {symbol.Kind}");
        text.AppendLine($"Metadata name: {symbol.MetadataName}");
        text.AppendLine($"Accessibility: {symbol.DeclaredAccessibility}");
        text.AppendLine($"Static: {symbol.IsStatic}");
        text.AppendLine($"Implicitly declared: {symbol.IsImplicitlyDeclared}");
        if (symbol.ContainingSymbol is { } containing)
            text.AppendLine($"Containing symbol: {containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
        if (symbol.ContainingAssembly is { } assembly)
            text.AppendLine($"Assembly: {assembly.Identity.GetDisplayName()}");
        string? type = SymbolType(symbol)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (type is not null) text.AppendLine($"Type: {type}");
        text.AppendLine("Locations:");
        foreach (Location location in symbol.Locations.Take(100))
            text.Append("  - ").AppendLine(LocationDisplay(location));
        AttributeData[] attributes = symbol.GetAttributes().Take(100).ToArray();
        if (attributes.Length > 0)
        {
            text.AppendLine("Attributes:");
            foreach (AttributeData attribute in attributes)
                text.Append("  - ").AppendLine(attribute.AttributeClass?.ToDisplayString() ?? "<unknown>");
        }
        string? documentation = symbol.GetDocumentationCommentXml(
            expandIncludes: false, cancellationToken: cancellationToken);
        bool truncated = false;
        if (!string.IsNullOrWhiteSpace(documentation))
        {
            text.AppendLine("Documentation XML:");
            AppendBounded(text, documentation, ref truncated);
        }
        return InspectionSuccess(request, $"Symbol · {symbol.Name}", text, origin, truncated);
    }

    private static async ValueTask<CodeIntelligenceInspectionResult> GeneratedInspectionAsync(
        CodeIntelligenceInspectionRequest request,
        Project project,
        CodeIntelligenceVirtualDocumentOrigin origin,
        CancellationToken cancellationToken)
    {
        SourceGeneratedDocument[] documents =
            (await project.GetSourceGeneratedDocumentsAsync(cancellationToken)).ToArray();
        StringBuilder text = Header("Generated source", request, origin);
        text.AppendLine($"Documents: {documents.Length}");
        text.AppendLine();
        bool truncated = documents.Length > MaximumGeneratedInspectionDocuments;
        foreach (SourceGeneratedDocument document in documents
                     .OrderBy(item => item.Name, StringComparer.Ordinal)
                     .Take(MaximumGeneratedInspectionDocuments))
        {
            SourceText source = await document.GetTextAsync(cancellationToken);
            text.AppendLine($"===== {document.Name} · {source.Length} characters =====");
            AppendBounded(text, source.ToString(), ref truncated);
            text.AppendLine();
            if (text.Length >= MaximumInspectionCharacters) break;
        }
        if (documents.Length == 0)
            text.AppendLine("The current project produced no source-generator documents.");
        return InspectionSuccess(request, "Generated source", text, origin, truncated);
    }

    private static async ValueTask<CodeIntelligenceInspectionResult> IlInspectionAsync(
        CodeIntelligenceInspectionRequest request,
        PreparedInteractive prepared,
        CodeIntelligenceVirtualDocumentOrigin origin,
        CancellationToken cancellationToken)
    {
        SemanticModel? model = await prepared.Document!.GetSemanticModelAsync(cancellationToken);
        SyntaxNode? root = await prepared.Document.GetSyntaxRootAsync(cancellationToken);
        ISymbol? symbol = model is null || root is null
            ? null : ResolveInspectionSymbol(model, root, prepared.Offset, cancellationToken);
        IMethodSymbol? method = symbol as IMethodSymbol ?? symbol?.ContainingSymbol as IMethodSymbol;
        if (method is null)
            return InspectionFailure(request, CodeIntelligenceResultState.Degraded,
                "il_method_required", "Place the caret in a method, constructor, or accessor to inspect IL.");
        Compilation? compilation = await prepared.Document.Project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
            return InspectionFailure(request, CodeIntelligenceResultState.Degraded,
                "il_compilation_unavailable", "The project did not produce a compilation.");

        using MemoryStream assembly = new();
        EmitResult emitted = compilation.Emit(assembly, cancellationToken: cancellationToken);
        if (!emitted.Success)
        {
            string errors = string.Join("\n", emitted.Diagnostics
                .Where(item => item.Severity == DiagnosticSeverity.Error)
                .Take(20).Select(item => item.ToString()));
            return InspectionFailure(request, CodeIntelligenceResultState.Degraded,
                "il_emit_failed", string.IsNullOrWhiteSpace(errors)
                    ? "The exact compilation could not be emitted." : errors);
        }
        assembly.Position = 0;
        using PEReader pe = new(assembly, PEStreamOptions.LeaveOpen);
        MetadataReader metadata = pe.GetMetadataReader();
        string typeName = MetadataTypeName(method.ContainingType);
        TypeDefinitionHandle typeHandle = metadata.TypeDefinitions.FirstOrDefault(handle =>
            MetadataTypeName(metadata, handle) == typeName);
        if (typeHandle.IsNil)
            return InspectionFailure(request, CodeIntelligenceResultState.Degraded,
                "il_type_unavailable", $"The emitted type '{typeName}' was not found.");
        TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
        MetadataTypeNameProvider typeNames = new(metadata);
        MethodDefinitionHandle[] candidates = type.GetMethods().Where(handle =>
        {
            MethodDefinition candidate = metadata.GetMethodDefinition(handle);
            if (metadata.GetString(candidate.Name) != method.MetadataName) return false;
            MethodSignature<string> signature = candidate.DecodeSignature(typeNames, genericContext: null);
            return signature.GenericParameterCount == method.Arity &&
                   signature.ParameterTypes.Length == method.Parameters.Length &&
                   signature.ParameterTypes.Select((parameter, index) =>
                       parameter == SymbolMetadataTypeName(method.Parameters[index])).All(item => item);
        }).ToArray();
        if (candidates.Length == 0)
            return InspectionFailure(request, CodeIntelligenceResultState.Degraded,
                "il_method_unavailable", "The selected method was not found in the emitted assembly.");

        StringBuilder text = Header("Intermediate Language", request, origin);
        text.AppendLine($"Selected symbol: {method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
        text.AppendLine($"Candidate bodies: {candidates.Length}");
        text.AppendLine();
        bool truncated = false;
        foreach (MethodDefinitionHandle handle in candidates.Take(16))
        {
            MethodDefinition definition = metadata.GetMethodDefinition(handle);
            int token = MetadataTokens.GetToken(handle);
            text.AppendLine($".method {metadata.GetString(definition.Name)} // token 0x{token:x8}");
            if (definition.RelativeVirtualAddress == 0)
            {
                text.AppendLine("  // No method body (abstract, extern, or runtime-provided).");
                continue;
            }
            MethodBodyBlock body = pe.GetMethodBody(definition.RelativeVirtualAddress);
            byte[] il = body.GetILBytes() ?? [];
            text.AppendLine($"  .maxstack {body.MaxStack}");
            AppendIl(text, metadata, il, ref truncated);
            text.AppendLine();
            if (text.Length >= MaximumInspectionCharacters) break;
        }
        if (candidates.Length > 16) truncated = true;
        return InspectionSuccess(request, $"IL · {method.Name}", text, origin, truncated);
    }

    private static void AppendIl(
        StringBuilder text,
        MetadataReader metadata,
        byte[] il,
        ref bool truncated)
    {
        int limit = Math.Min(il.Length, MaximumIlBytesPerMethod);
        int offset = 0;
        while (offset < limit && text.Length < MaximumInspectionCharacters)
        {
            int instructionOffset = offset;
            ushort value = il[offset++];
            if (value == 0xfe && offset < limit) value = (ushort)(0xfe00 | il[offset++]);
            if (!IlOpCodes.TryGetValue(value, out OpCode opCode))
            {
                text.AppendLine($"  IL_{instructionOffset:x4}: <unknown 0x{value:x4}>");
                continue;
            }
            text.Append("  IL_").Append(instructionOffset.ToString("x4")).Append(": ")
                .Append(opCode.Name);
            AppendOperand(text, metadata, il, limit, ref offset, opCode.OperandType,
                instructionOffset);
            text.AppendLine();
        }
        if (offset < il.Length || text.Length >= MaximumInspectionCharacters) truncated = true;
    }

    private static void AppendOperand(
        StringBuilder text,
        MetadataReader metadata,
        byte[] il,
        int limit,
        ref int offset,
        OperandType type,
        int instructionOffset)
    {
        int remaining = limit - offset;
        switch (type)
        {
            case OperandType.InlineNone:
                return;
            case OperandType.ShortInlineI:
                if (remaining >= 1) text.Append(' ').Append(unchecked((sbyte)il[offset++]));
                return;
            case OperandType.ShortInlineVar:
                if (remaining >= 1) text.Append(" V_").Append(il[offset++]);
                return;
            case OperandType.InlineVar:
                if (remaining >= 2)
                {
                    text.Append(" V_").Append(BinaryPrimitives.ReadUInt16LittleEndian(il.AsSpan(offset)));
                    offset += 2;
                }
                return;
            case OperandType.ShortInlineBrTarget:
                if (remaining >= 1)
                {
                    int delta = unchecked((sbyte)il[offset++]);
                    text.Append(" IL_").Append((offset + delta).ToString("x4"));
                }
                return;
            case OperandType.InlineBrTarget:
                if (remaining >= 4)
                {
                    int delta = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset));
                    offset += 4;
                    text.Append(" IL_").Append((offset + delta).ToString("x4"));
                }
                return;
            case OperandType.InlineI:
                if (remaining >= 4)
                {
                    text.Append(' ').Append(BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset)));
                    offset += 4;
                }
                return;
            case OperandType.InlineI8:
                if (remaining >= 8)
                {
                    text.Append(' ').Append(BinaryPrimitives.ReadInt64LittleEndian(il.AsSpan(offset)));
                    offset += 8;
                }
                return;
            case OperandType.ShortInlineR:
                if (remaining >= 4)
                {
                    text.Append(' ').Append(BitConverter.ToSingle(il, offset));
                    offset += 4;
                }
                return;
            case OperandType.InlineR:
                if (remaining >= 8)
                {
                    text.Append(' ').Append(BitConverter.ToDouble(il, offset));
                    offset += 8;
                }
                return;
            case OperandType.InlineSwitch:
                if (remaining >= 4)
                {
                    int count = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset));
                    offset += 4;
                    if (count >= 0 && count <= 10_000 && limit - offset >= count * 4)
                    {
                        int baseOffset = offset + count * 4;
                        text.Append(" (");
                        for (int index = 0; index < count; index++)
                        {
                            if (index > 0) text.Append(", ");
                            int delta = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset));
                            offset += 4;
                            text.Append("IL_").Append((baseOffset + delta).ToString("x4"));
                        }
                        text.Append(')');
                    }
                }
                return;
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
                if (remaining >= 4)
                {
                    int token = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset));
                    offset += 4;
                    text.Append(" 0x").Append(token.ToString("x8"));
                    string display = MetadataTokenDisplay(metadata, token, type);
                    if (display.Length > 0) text.Append(" // ").Append(display);
                }
                return;
            default:
                text.Append(" // unsupported operand at IL_").Append(instructionOffset.ToString("x4"));
                return;
        }
    }

    private static string MetadataTokenDisplay(
        MetadataReader reader,
        int token,
        OperandType operandType)
    {
        try
        {
            if (operandType == OperandType.InlineString)
                return '"' + OneLine(reader.GetUserString(
                    MetadataTokens.UserStringHandle(token & 0x00ffffff)), 160) + '"';
            EntityHandle handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => reader.GetString(
                    reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
                HandleKind.TypeReference => reader.GetString(
                    reader.GetTypeReference((TypeReferenceHandle)handle).Name),
                HandleKind.MethodDefinition => reader.GetString(
                    reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
                HandleKind.MemberReference => reader.GetString(
                    reader.GetMemberReference((MemberReferenceHandle)handle).Name),
                HandleKind.FieldDefinition => reader.GetString(
                    reader.GetFieldDefinition((FieldDefinitionHandle)handle).Name),
                _ => handle.Kind.ToString(),
            };
        }
        catch (Exception exception) when (exception is BadImageFormatException or ArgumentException)
        {
            return "invalid metadata token";
        }
    }

    private static ISymbol? ResolveInspectionSymbol(
        SemanticModel model,
        SyntaxNode root,
        int offset,
        CancellationToken cancellationToken)
    {
        SyntaxNode? node = root.FindToken(offset).Parent;
        foreach (SyntaxNode candidate in node?.AncestorsAndSelf() ?? [])
        {
            ISymbol? declared = model.GetDeclaredSymbol(candidate, cancellationToken);
            if (declared is not null) return declared;
            SymbolInfo info = model.GetSymbolInfo(candidate, cancellationToken);
            if (info.Symbol is not null) return info.Symbol;
        }
        return model.GetEnclosingSymbol(offset, cancellationToken);
    }

    private static bool IsInspectionScope(SyntaxNode node) => node is
        Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax or
        Microsoft.CodeAnalysis.CSharp.Syntax.AccessorDeclarationSyntax or
        Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax;

    private static ITypeSymbol? SymbolType(ISymbol symbol) => symbol switch
    {
        IMethodSymbol value => value.ReturnType,
        IPropertySymbol value => value.Type,
        IFieldSymbol value => value.Type,
        IEventSymbol value => value.Type,
        ILocalSymbol value => value.Type,
        IParameterSymbol value => value.Type,
        _ => symbol as ITypeSymbol,
    };

    private static string LocationDisplay(Location location)
    {
        if (!location.IsInSource || location.SourceTree?.FilePath is not { } path)
            return location.Kind.ToString();
        FileLinePositionSpan span = location.GetLineSpan();
        return $"{path}:{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}";
    }

    private static StringBuilder Header(
        string label,
        CodeIntelligenceInspectionRequest request,
        CodeIntelligenceVirtualDocumentOrigin origin) => new StringBuilder()
        .AppendLine($"{label} · read-only")
        .AppendLine($"Source: {request.Snapshot.Path.Value} · buffer {request.Snapshot.BufferVersion.Value}")
        .AppendLine($"Project: {origin.Project.Value} · version {origin.ProjectVersion.Value}")
        .AppendLine($"Target: {origin.TargetFramework.Value} · {origin.Configuration.Value}")
        .AppendLine($"Assembly: {origin.Assembly.Value}")
        .AppendLine($"Compilation: {origin.Compilation.Value}")
        .AppendLine();

    private static void AppendBounded(StringBuilder target, string value, ref bool truncated)
    {
        int remaining = MaximumInspectionCharacters - target.Length;
        if (remaining <= 0) { truncated = true; return; }
        if (value.Length <= remaining) { target.AppendLine(value); return; }
        target.Append(value.AsSpan(0, remaining));
        truncated = true;
    }

    private static string OneLine(string value, int maximum)
    {
        string escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return escaped.Length <= maximum ? escaped : escaped[..maximum] + "…";
    }

    private static string MetadataTypeName(INamedTypeSymbol type)
    {
        Stack<string> names = new();
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
            names.Push(current.MetadataName);
        string prefix = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty : type.ContainingNamespace.ToDisplayString() + ".";
        return prefix + string.Join('+', names);
    }

    private static string MetadataTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        Stack<string> names = new();
        TypeDefinition current = reader.GetTypeDefinition(handle);
        names.Push(reader.GetString(current.Name));
        TypeDefinitionHandle parent = current.GetDeclaringType();
        while (!parent.IsNil)
        {
            current = reader.GetTypeDefinition(parent);
            names.Push(reader.GetString(current.Name));
            parent = current.GetDeclaringType();
        }
        string namespaced = string.Join('+', names);
        string value = reader.GetString(current.Namespace);
        return string.IsNullOrEmpty(value) ? namespaced : value + "." + namespaced;
    }

    private static string MetadataTypeName(MetadataReader reader, TypeReferenceHandle handle)
    {
        Stack<string> names = new();
        TypeReference current = reader.GetTypeReference(handle);
        names.Push(reader.GetString(current.Name));
        EntityHandle scope = current.ResolutionScope;
        while (scope.Kind is HandleKind.TypeReference)
        {
            current = reader.GetTypeReference((TypeReferenceHandle)scope);
            names.Push(reader.GetString(current.Name));
            scope = current.ResolutionScope;
        }
        string namespaced = string.Join('+', names);
        string value = reader.GetString(current.Namespace);
        return string.IsNullOrEmpty(value) ? namespaced : value + "." + namespaced;
    }

    private static string SymbolMetadataTypeName(IParameterSymbol parameter) =>
        SymbolMetadataTypeName(parameter.Type) +
        (parameter.RefKind is RefKind.None ? string.Empty : "&");

    private static string SymbolMetadataTypeName(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => SymbolMetadataTypeName(array.ElementType) +
            (array.Rank == 1 ? "[]" : "[" + new string(',', array.Rank - 1) + "]"),
        IPointerTypeSymbol pointer => SymbolMetadataTypeName(pointer.PointedAtType) + "*",
        ITypeParameterSymbol parameter when parameter.TypeParameterKind is TypeParameterKind.Method =>
            "!!" + parameter.Ordinal,
        ITypeParameterSymbol parameter => "!" + parameter.Ordinal,
        INamedTypeSymbol named => NamedMetadataTypeName(named),
        _ => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal),
    };

    private static string NamedMetadataTypeName(INamedTypeSymbol type)
    {
        string definition = MetadataTypeName(type.OriginalDefinition);
        return type.IsGenericType && !SymbolEqualityComparer.Default.Equals(type, type.OriginalDefinition)
            ? definition + "<" + string.Join(',', type.TypeArguments.Select(SymbolMetadataTypeName)) + ">"
            : definition;
    }

    private static IReadOnlyDictionary<ushort, OpCode> BuildOpCodes() =>
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => unchecked((ushort)code.Value));

    private sealed class MetadataTypeNameProvider(MetadataReader reader)
        : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) =>
            elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> arguments) =>
            genericType + "<" + string.Join(',', arguments) + ">";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifierType, string unmodifiedType, bool isRequired) =>
            unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.Void => "System.Void",
            _ => typeCode.ToString(),
        };
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(
            MetadataReader ignored, TypeDefinitionHandle handle, byte rawTypeKind) =>
            MetadataTypeName(reader, handle);
        public string GetTypeFromReference(
            MetadataReader ignored, TypeReferenceHandle handle, byte rawTypeKind) =>
            MetadataTypeName(reader, handle);
        public string GetTypeFromSpecification(
            MetadataReader ignored,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) => reader.GetTypeSpecification(handle)
            .DecodeSignature(this, genericContext);
    }

    private static CodeIntelligenceInspectionResult InspectionSuccess(
        CodeIntelligenceInspectionRequest request,
        string title,
        StringBuilder text,
        CodeIntelligenceVirtualDocumentOrigin origin,
        bool truncated) => new(
            request.Snapshot.ContextId, request.Snapshot.SessionId, request.Snapshot.Path,
            request.Snapshot.BufferVersion, CodeIntelligenceResultState.Ready, request.Kind,
            new(title), new(text.ToString()), origin, IsReadOnly: true, truncated, []);

    private static CodeIntelligenceInspectionResult InspectionFailure(
        CodeIntelligenceInspectionRequest request,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(
            request.Snapshot.ContextId, request.Snapshot.SessionId, request.Snapshot.Path,
            request.Snapshot.BufferVersion, state, request.Kind, Title: null, Text: null,
            Origin: null, IsReadOnly: true, IsTruncated: false,
            [new(new(code), new(Bound(message, MaximumIssueLength)))]);
}
