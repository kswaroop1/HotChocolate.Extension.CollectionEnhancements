using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace HotChocolate.Extension.CollectionEnhancements.Generators;

[ExcludeFromCodeCoverage]
[Generator]
public sealed class CollectionEnhancementSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelProvider = context.CompilationProvider
            .Select(static (compilation, _) => ModelDiscovery.Discover(compilation));

        context.RegisterSourceOutput(modelProvider, static (productionContext, model) =>
        {
            if (model.ObjectTypes.Length == 0)
            {
                return;
            }

            productionContext.AddSource(
                "CollectionEnhancement.Generated.g.cs",
                SourceText.From(SourceEmitter.Emit(model), Encoding.UTF8));
        });
    }

    internal static class ModelDiscovery
    {
        private static readonly SymbolDisplayFormat FullyQualifiedTypeFormat =
            new(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

        private const string CollectionEnhancementModelAttributeName =
            "HotChocolate.Extension.CollectionEnhancements.CollectionEnhancementModelAttribute";

        internal enum MemberKindInfo
        {
            Property,
            Method
        }

        public static GenerationModel Discover(Compilation compilation)
        {
            var attributeSymbol = compilation.GetTypeByMetadataName(CollectionEnhancementModelAttributeName);
            var roots = EnumerateTypes(compilation.Assembly.GlobalNamespace)
                .Where(type => IsRoot(type, attributeSymbol))
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<INamedTypeSymbol>()
                .ToArray();

            if (roots.Length == 0)
            {
                return GenerationModel.Empty;
            }

            var discoveredTypes = new Dictionary<INamedTypeSymbol, ObjectTypeInfo>(SymbolEqualityComparer.Default);

            foreach (var root in roots)
            {
                VisitType(root, IsQueryRoot(root, attributeSymbol), discoveredTypes);
            }

            return new GenerationModel(
                discoveredTypes.Values
                    .OrderBy(type => type.GraphQlTypeName, StringComparer.Ordinal)
                    .ToImmutableArray());
        }

        private static void VisitType(
            INamedTypeSymbol type,
            bool isQueryRoot,
            IDictionary<INamedTypeSymbol, ObjectTypeInfo> discoveredTypes)
        {
            if (discoveredTypes.TryGetValue(type, out var existing))
            {
                if (!isQueryRoot || existing.IsQueryRoot)
                {
                    return;
                }

                discoveredTypes.Remove(type);
            }

            var scalarFields = ImmutableArray.CreateBuilder<FieldInfo>();
            var objectFields = ImmutableArray.CreateBuilder<FieldInfo>();
            var collectionFields = ImmutableArray.CreateBuilder<CollectionFieldInfo>();
            var members = isQueryRoot ? GetQueryMembers(type) : GetObjectMembers(type);
            var graphQlTypeName = isQueryRoot ? "Query" : type.Name;

            discoveredTypes[type] = new ObjectTypeInfo(
                GetTypeName(type),
                graphQlTypeName,
                isQueryRoot,
                [],
                [],
                []);

            foreach (var member in members)
            {
                var memberType = GetMemberType(member);
                var graphQlName = GetFieldName(member);

                if (TryGetCollectionElementType(memberType, out var elementType) &&
                    elementType is not null &&
                    !IsScalar(elementType))
                {
                    var elementNamedType = (INamedTypeSymbol)elementType;
                    var hostTypeName = graphQlTypeName;
                    var elementTypeName = elementNamedType.Name;
                    var typePrefix = hostTypeName + ToPascalCase(graphQlName);
                    var flatRowTypeName = typePrefix + "GeneratedFlatRow";
                    var flatPaths = DiscoverFlatPaths(elementNamedType);

                    collectionFields.Add(new CollectionFieldInfo(
                        member.Name,
                        GetMemberKind(member),
                        graphQlName,
                        GetTypeName(memberType),
                        GetTypeName(elementNamedType),
                        hostTypeName,
                        elementTypeName,
                        flatRowTypeName,
                        flatPaths));

                    VisitType(elementNamedType, isQueryRoot: false, discoveredTypes);
                    continue;
                }

                if (IsScalar(memberType))
                {
                    scalarFields.Add(new FieldInfo(
                        member.Name,
                        GetMemberKind(member),
                        graphQlName,
                        GetTypeName(memberType),
                        GetFlatRowPropertyTypeName(memberType)));
                    continue;
                }

                if (memberType is INamedTypeSymbol namedType &&
                    IsObjectType(namedType))
                {
                    objectFields.Add(new FieldInfo(
                        member.Name,
                        GetMemberKind(member),
                        graphQlName,
                        GetTypeName(namedType),
                        GetTypeName(namedType)));

                    VisitType(namedType, isQueryRoot: false, discoveredTypes);
                }
            }

            discoveredTypes[type] = new ObjectTypeInfo(
                GetTypeName(type),
                graphQlTypeName,
                isQueryRoot,
                scalarFields.ToImmutable(),
                objectFields.ToImmutable(),
                collectionFields.ToImmutable());
        }

        private static ImmutableArray<FlatPathInfo> DiscoverFlatPaths(INamedTypeSymbol elementType)
        {
            var discoveredPaths = new List<PathCandidate>();
            DiscoverFlatPathsCore(
                elementType,
                [],
                [],
                discoveredPaths,
                new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));

            if (discoveredPaths.Count == 0)
            {
                return [];
            }

            var groupedByTerminalSegment = discoveredPaths
                .GroupBy(
                    path => Singularize(GetFieldName(path.MemberSegments[path.MemberSegments.Length - 1])),
                    StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

            return discoveredPaths
                .Select(path =>
                {
                    var terminalSegment = Singularize(GetFieldName(path.MemberSegments[path.MemberSegments.Length - 1]));
                    var prefix = groupedByTerminalSegment[terminalSegment].Length == 1
                        ? terminalSegment
                        : GetMinimalUniquePrefix(path, groupedByTerminalSegment[terminalSegment]);

                    return new FlatPathInfo(
                        path.Path,
                        prefix,
                        GetTypeName(path.TerminalType),
                        path.TerminalType.Name,
                        path.MemberSegments.Select(member => member.Name).ToImmutableArray());
                })
                .OrderBy(path => path.Path, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        private static void DiscoverFlatPathsCore(
            INamedTypeSymbol currentType,
            ImmutableArray<string> pathSegments,
            ImmutableArray<ISymbol> memberSegments,
            ICollection<PathCandidate> discoveredPaths,
            ISet<INamedTypeSymbol> visitedTypes)
        {
            if (!visitedTypes.Add(currentType))
            {
                return;
            }

            foreach (var property in GetObjectMembers(currentType).OfType<IPropertySymbol>())
            {
                var propertyType = property.Type;

                if (TryGetCollectionElementType(propertyType, out var elementType) &&
                    elementType is INamedTypeSymbol elementNamedType &&
                    !IsScalar(elementNamedType))
                {
                    var nextPathSegments = pathSegments.Add(GetFieldName(property));
                    var nextMemberSegments = memberSegments.Add(property);
                    discoveredPaths.Add(new PathCandidate(
                        string.Join(".", nextPathSegments),
                        nextMemberSegments,
                        elementNamedType));
                    DiscoverFlatPathsCore(elementNamedType, nextPathSegments, nextMemberSegments, discoveredPaths, visitedTypes);
                    continue;
                }

                if (propertyType is INamedTypeSymbol objectNamedType && IsObjectType(objectNamedType))
                {
                    DiscoverFlatPathsCore(
                        objectNamedType,
                        pathSegments.Add(GetFieldName(property)),
                        memberSegments.Add(property),
                        discoveredPaths,
                        visitedTypes);
                }
            }

            visitedTypes.Remove(currentType);
        }

        private static string GetMinimalUniquePrefix(PathCandidate target, IReadOnlyList<PathCandidate> competingPaths)
        {
            var names = target.MemberSegments
                .Select(member => Singularize(GetFieldName(member)))
                .ToArray();

            for (var length = 1; length <= names.Length; length++)
            {
                var prefix = ToCamelCase(string.Concat(TakeTail(names, length).Select(ToPascalCase)));
                var duplicates = competingPaths.Count(path =>
                    ToCamelCase(
                        string.Concat(TakeTail(
                            path.MemberSegments
                                .Select(member => Singularize(GetFieldName(member)))
                                .ToArray(),
                            length)
                            .Select(ToPascalCase))) == prefix);

                if (duplicates == 1)
                {
                    return prefix;
                }
            }

            return ToCamelCase(string.Concat(names.Select(ToPascalCase)));
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
        {
            foreach (var member in namespaceSymbol.GetMembers())
            {
                switch (member)
                {
                    case INamespaceSymbol childNamespace:
                        foreach (var child in EnumerateTypes(childNamespace))
                        {
                            yield return child;
                        }

                        break;

                    case INamedTypeSymbol namedType:
                        yield return namedType;

                        foreach (var nested in EnumerateNestedTypes(namedType))
                        {
                            yield return nested;
                        }

                        break;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol containingType)
        {
            foreach (var nestedType in containingType.GetTypeMembers())
            {
                yield return nestedType;

                foreach (var child in EnumerateNestedTypes(nestedType))
                {
                    yield return child;
                }
            }
        }

        private static bool IsRoot(INamedTypeSymbol type, INamedTypeSymbol? attributeSymbol) =>
            type is { TypeKind: TypeKind.Class or TypeKind.Struct, IsAbstract: false }
            && (type.Name == "Query" || HasCollectionEnhancementModelAttribute(type, attributeSymbol));

        private static bool IsQueryRoot(INamedTypeSymbol type, INamedTypeSymbol? attributeSymbol)
        {
            if (type.Name == "Query")
            {
                return true;
            }

            var attribute = GetCollectionEnhancementAttribute(type, attributeSymbol);
            if (attribute is null)
            {
                return false;
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (argument is { Key: "IsQueryRoot", Value.Value: bool isQueryRoot })
                {
                    return isQueryRoot;
                }
            }

            return false;
        }

        private static bool HasCollectionEnhancementModelAttribute(INamedTypeSymbol type, INamedTypeSymbol? attributeSymbol) =>
            GetCollectionEnhancementAttribute(type, attributeSymbol) is not null;

        [ExcludeFromCodeCoverage]
        private static AttributeData? GetCollectionEnhancementAttribute(INamedTypeSymbol type, INamedTypeSymbol? attributeSymbol) =>
            type.GetAttributes().FirstOrDefault(attribute =>
                attribute.AttributeClass is not null &&
                (attributeSymbol is not null
                    ? SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol)
                    : attribute.AttributeClass.ToDisplayString() == CollectionEnhancementModelAttributeName));

        private static IEnumerable<ISymbol> GetQueryMembers(INamedTypeSymbol queryType) =>
            queryType.GetMembers()
                .Where(member =>
                    member is IMethodSymbol
                    {
                        MethodKind: MethodKind.Ordinary,
                        DeclaredAccessibility: Accessibility.Public,
                        IsStatic: false,
                        IsImplicitlyDeclared: false
                    } method
                    && method.Name.IndexOf('<') < 0)
                .Where(member => ((IMethodSymbol)member).Parameters.All(IsSupportedQueryParameter));

        private static IEnumerable<ISymbol> GetObjectMembers(INamedTypeSymbol objectType) =>
            objectType.GetMembers()
                .Where(member => member is IPropertySymbol { DeclaredAccessibility: Accessibility.Public, IsStatic: false } property && property.GetMethod is not null);

        [ExcludeFromCodeCoverage]
        private static bool IsSupportedQueryParameter(IParameterSymbol parameter) =>
            parameter.GetAttributes()
                .Select(attribute => attribute.AttributeClass?.Name)
                .Any(name => name is "ServiceAttribute" or "ParentAttribute");

        private static ITypeSymbol GetMemberType(ISymbol member) =>
            member switch
            {
                IPropertySymbol property => property.Type,
                IMethodSymbol method => method.ReturnType,
                _ => throw new NotSupportedException($"Unsupported member {member}.")
            };

        private static MemberKindInfo GetMemberKind(ISymbol member) =>
            member switch
            {
                IPropertySymbol => MemberKindInfo.Property,
                IMethodSymbol => MemberKindInfo.Method,
                _ => throw new NotSupportedException($"Unsupported member {member}.")
            };

        private static bool IsScalar(ITypeSymbol type)
        {
            var actualType = UnwrapNullable(type);
            return actualType.TypeKind == TypeKind.Enum
                || actualType.SpecialType is SpecialType.System_Boolean
                    or SpecialType.System_Byte
                    or SpecialType.System_Int16
                    or SpecialType.System_Int32
                    or SpecialType.System_Int64
                    or SpecialType.System_Single
                    or SpecialType.System_Double
                    or SpecialType.System_Decimal
                    or SpecialType.System_String
                || actualType.ToDisplayString() is "System.DateOnly" or "System.DateTime" or "System.Guid";
        }

        private static bool IsObjectType(INamedTypeSymbol type) =>
            type is { IsAbstract: false }
            && type.SpecialType == SpecialType.None
            && type.Name != "Object"
            && type.ContainingNamespace is not null;

        private static bool TryGetCollectionElementType(ITypeSymbol type, out ITypeSymbol? elementType)
        {
            if (type.SpecialType == SpecialType.System_String)
            {
                elementType = null;
                return false;
            }

            if (type is IArrayTypeSymbol arrayType)
            {
                elementType = arrayType.ElementType;
                return true;
            }

            if (type is INamedTypeSymbol namedType &&
                namedType.IsGenericType &&
                namedType.ConstructedFrom.ToDisplayString() == "System.Linq.IQueryable<T>")
            {
                elementType = namedType.TypeArguments[0];
                return true;
            }

            var enumerableInterface = type.AllInterfaces.FirstOrDefault(candidate =>
                candidate is INamedTypeSymbol { IsGenericType: true } interfaceType &&
                interfaceType.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>");

            if (enumerableInterface is INamedTypeSymbol enumerableNamedType)
            {
                elementType = enumerableNamedType.TypeArguments[0];
                return true;
            }

            elementType = null;
            return false;
        }

        private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
            type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType
                ? nullableType.TypeArguments[0]
                : type;

        private static string GetFieldName(ISymbol member) =>
            member switch
            {
                IMethodSymbol method => ToCamelCase(TrimGetPrefix(method.Name)),
                IPropertySymbol property => ToCamelCase(property.Name),
                _ => ToCamelCase(member.Name)
            };

        private static string GetTypeName(ITypeSymbol type) =>
            type.ToDisplayString(FullyQualifiedTypeFormat);

        private static string GetFlatRowPropertyTypeName(ITypeSymbol type)
        {
            var typeName = GetTypeName(type);
            return type.IsReferenceType
                ? typeName + "?"
                : typeName;
        }

        private static string TrimGetPrefix(string value) =>
            value.StartsWith("Get", StringComparison.Ordinal) && value.Length > 3
                ? value.Substring(3)
                : value;

        private static string ToCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length == 1
                ? value.ToLowerInvariant()
                : char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        private static string ToPascalCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length == 1
                ? value.ToUpperInvariant()
                : char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static string Singularize(string value)
        {
            if (value.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
            {
                return value.Substring(0, value.Length - 3) + "y";
            }

            if (value.EndsWith("ses", StringComparison.OrdinalIgnoreCase))
            {
                return value.Substring(0, value.Length - 2);
            }

            if (value.EndsWith("s", StringComparison.Ordinal) && value.Length > 1)
            {
                return value.Substring(0, value.Length - 1);
            }

            return value;
        }

        private static IEnumerable<string> TakeTail(IReadOnlyList<string> values, int length)
        {
            var startIndex = values.Count - length;
            for (var index = startIndex; index < values.Count; index++)
            {
                yield return values[index];
            }
        }

        internal sealed record GenerationModel(ImmutableArray<ObjectTypeInfo> ObjectTypes)
        {
            public static GenerationModel Empty { get; } = new([]);
        }

        internal sealed record ObjectTypeInfo(
            string TypeName,
            string GraphQlTypeName,
            bool IsQueryRoot,
            ImmutableArray<FieldInfo> ScalarFields,
            ImmutableArray<FieldInfo> ObjectFields,
            ImmutableArray<CollectionFieldInfo> CollectionFields);

        internal sealed record FieldInfo(
            string MemberName,
            MemberKindInfo MemberKind,
            string GraphQlName,
            string TypeName,
            string FlatRowPropertyTypeName);

        internal sealed record CollectionFieldInfo(
            string MemberName,
            MemberKindInfo MemberKind,
            string GraphQlName,
            string TypeName,
            string ElementTypeName,
            string HostTypeName,
            string ElementGraphQlTypeName,
            string FlatRowTypeName,
            ImmutableArray<FlatPathInfo> FlatPaths);

        internal sealed record FlatPathInfo(
            string Path,
            string Prefix,
            string TerminalTypeName,
            string TerminalGraphQlTypeName,
            ImmutableArray<string> SegmentMemberNames);

        private sealed record PathCandidate(
            string Path,
            ImmutableArray<ISymbol> MemberSegments,
            INamedTypeSymbol TerminalType);
    }

    internal static class SourceEmitter
    {
        public static string Emit(ModelDiscovery.GenerationModel model)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
            builder.AppendLine("namespace HotChocolate.Extension.CollectionEnhancements.Generated;");
            builder.AppendLine();

            foreach (var objectType in model.ObjectTypes)
            {
                foreach (var collectionField in objectType.CollectionFields)
                {
                    EmitFlatRowType(builder, model, objectType, collectionField);
                }
            }

            builder.AppendLine("public sealed class CollectionEnhancementGeneratedModelProvider : ICollectionEnhancementGeneratedModelProvider");
            builder.AppendLine("{");
            builder.AppendLine("    public System.Collections.Generic.IReadOnlyList<CollectionEnhancementGeneratedObjectType> GetObjectTypes() => s_objectTypes;");
            builder.AppendLine();
            builder.AppendLine("    private static readonly CollectionEnhancementGeneratedObjectType[] s_objectTypes =");
            builder.AppendLine("    [");

            for (var index = 0; index < model.ObjectTypes.Length; index++)
            {
                var objectType = model.ObjectTypes[index];
                builder.Append("        new CollectionEnhancementGeneratedObjectType(");
                AppendTypeOf(builder, objectType.TypeName);
                builder.Append(", ");
                builder.Append(SymbolDisplay.FormatLiteral(objectType.GraphQlTypeName, quote: true));
                builder.Append(", ");
                builder.Append(objectType.IsQueryRoot ? "true" : "false");
                builder.Append(", ");
                EmitFieldArray(builder, objectType.ScalarFields, "CollectionEnhancementGeneratedScalarField");
                builder.Append(", ");
                EmitFieldArray(builder, objectType.ObjectFields, "CollectionEnhancementGeneratedObjectField");
                builder.Append(", ");
                EmitCollectionArray(builder, objectType.CollectionFields);
                builder.Append(')');

                builder.AppendLine(index == model.ObjectTypes.Length - 1 ? string.Empty : ",");
            }

            builder.AppendLine("    ];");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void EmitFlatRowType(
            StringBuilder builder,
            ModelDiscovery.GenerationModel model,
            ModelDiscovery.ObjectTypeInfo objectType,
            ModelDiscovery.CollectionFieldInfo collectionField)
        {
            var elementType = model.ObjectTypes.First(type => type.TypeName == collectionField.ElementTypeName);
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var scalarField in elementType.ScalarFields)
            {
                properties[ToPascalCase(scalarField.GraphQlName)] = scalarField.FlatRowPropertyTypeName;
            }

            foreach (var path in collectionField.FlatPaths)
            {
                var terminalType = model.ObjectTypes.FirstOrDefault(type => type.TypeName == path.TerminalTypeName);
                if (terminalType is null)
                {
                    continue;
                }

                foreach (var scalarField in terminalType.ScalarFields)
                {
                    properties[ToPascalCase(path.Prefix + ToPascalCase(scalarField.GraphQlName))] = scalarField.FlatRowPropertyTypeName;
                }
            }

            builder.Append("public sealed partial record class ");
            builder.Append(collectionField.FlatRowTypeName);
            builder.AppendLine();
            builder.AppendLine("{");
            builder.AppendLine($"    public {collectionField.FlatRowTypeName}() {{ }}");

            foreach (var property in properties.OrderBy(property => property.Key, StringComparer.Ordinal))
            {
                builder.Append("    public ");
                builder.Append(property.Value);
                builder.Append(' ');
                builder.Append(property.Key);
                builder.AppendLine(" { get; init; }");
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void EmitFieldArray(
            StringBuilder builder,
            ImmutableArray<ModelDiscovery.FieldInfo> fields,
            string generatedFieldType)
        {
            builder.Append("new ");
            builder.Append(generatedFieldType);
            builder.Append("[] { ");

            for (var index = 0; index < fields.Length; index++)
            {
                var field = fields[index];
                builder.Append("new ");
                builder.Append(generatedFieldType);
                builder.Append('(');
                builder.Append(SymbolDisplay.FormatLiteral(field.MemberName, quote: true));
                builder.Append(", CollectionEnhancementGeneratedMemberKind.");
                builder.Append(field.MemberKind);
                builder.Append(", ");
                builder.Append(SymbolDisplay.FormatLiteral(field.GraphQlName, quote: true));
                builder.Append(", ");
                AppendTypeOf(builder, field.TypeName);
                builder.Append(')');

                if (index < fields.Length - 1)
                {
                    builder.Append(", ");
                }
            }

            builder.Append(" }");
        }

        private static void EmitCollectionArray(
            StringBuilder builder,
            ImmutableArray<ModelDiscovery.CollectionFieldInfo> collectionFields)
        {
            builder.Append("new CollectionEnhancementGeneratedCollectionField[] { ");

            for (var index = 0; index < collectionFields.Length; index++)
            {
                var field = collectionFields[index];
                builder.Append("new CollectionEnhancementGeneratedCollectionField(");
                builder.Append(SymbolDisplay.FormatLiteral(field.MemberName, quote: true));
                builder.Append(", CollectionEnhancementGeneratedMemberKind.");
                builder.Append(field.MemberKind);
                builder.Append(", ");
                builder.Append(SymbolDisplay.FormatLiteral(field.GraphQlName, quote: true));
                builder.Append(", ");
                AppendTypeOf(builder, field.TypeName);
                builder.Append(", ");
                AppendTypeOf(builder, field.ElementTypeName);
                builder.Append(", ");
                builder.Append(SymbolDisplay.FormatLiteral(field.HostTypeName, quote: true));
                builder.Append(", ");
                builder.Append(SymbolDisplay.FormatLiteral(field.ElementGraphQlTypeName, quote: true));
                builder.Append(", typeof(");
                builder.Append("global::HotChocolate.Extension.CollectionEnhancements.Generated.");
                builder.Append(field.FlatRowTypeName);
                builder.Append("), ");
                EmitFlatPathArray(builder, field.FlatPaths);
                builder.Append(')');

                if (index < collectionFields.Length - 1)
                {
                    builder.Append(", ");
                }
            }

            builder.Append(" }");
        }

        private static void EmitFlatPathArray(
            StringBuilder builder,
            ImmutableArray<ModelDiscovery.FlatPathInfo> flatPaths)
        {
            builder.Append("new CollectionEnhancementGeneratedFlatPath[] { ");

            for (var index = 0; index < flatPaths.Length; index++)
            {
                var path = flatPaths[index];
                builder.Append("new CollectionEnhancementGeneratedFlatPath(");
                builder.Append(SymbolDisplay.FormatLiteral(path.Path, quote: true));
                builder.Append(", ");
                builder.Append(SymbolDisplay.FormatLiteral(path.Prefix, quote: true));
                builder.Append(", ");
                AppendTypeOf(builder, path.TerminalTypeName);
                builder.Append(", ");
                builder.Append(SymbolDisplay.FormatLiteral(path.TerminalGraphQlTypeName, quote: true));
                builder.Append(", new string[] { ");

                for (var segmentIndex = 0; segmentIndex < path.SegmentMemberNames.Length; segmentIndex++)
                {
                    builder.Append(SymbolDisplay.FormatLiteral(path.SegmentMemberNames[segmentIndex], quote: true));
                    if (segmentIndex < path.SegmentMemberNames.Length - 1)
                    {
                        builder.Append(", ");
                    }
                }

                builder.Append(" })");

                if (index < flatPaths.Length - 1)
                {
                    builder.Append(", ");
                }
            }

            builder.Append(" }");
        }

        private static string ToPascalCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length == 1
                ? value.ToUpperInvariant()
                : char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static void AppendTypeOf(StringBuilder builder, string typeName)
        {
            builder.Append("typeof(");
            builder.Append(typeName);
            builder.Append(')');
        }
    }
}
