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
                NormalizeGraphQlTypeNames(discoveredTypes)
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
            var graphQlTypeName = isQueryRoot ? "Query" : GetGraphQlTypeName(type);

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
                    IsEnhancementObjectType(elementType))
                {
                    var elementNamedType = (INamedTypeSymbol)elementType;
                    var hostTypeName = graphQlTypeName;
                    var elementTypeName = GetGraphQlTypeName(elementNamedType);
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
                    IsEnhancementObjectType(namedType))
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

        private static ImmutableArray<ObjectTypeInfo> NormalizeGraphQlTypeNames(
            IReadOnlyDictionary<INamedTypeSymbol, ObjectTypeInfo> discoveredTypes)
        {
            var nameMap = BuildUniqueGraphQlTypeNameMap(discoveredTypes);
            var nameMapByTypeName = nameMap.ToDictionary(
                pair => pair.Key.ToDisplayString(FullyQualifiedTypeFormat),
                pair => pair.Value,
                StringComparer.Ordinal);

            return discoveredTypes
                .Select(pair =>
                {
                    var type = pair.Key;
                    var objectType = pair.Value;
                    var graphQlTypeName = objectType.IsQueryRoot ? "Query" : nameMap[type];

                    return objectType with
                    {
                        GraphQlTypeName = graphQlTypeName,
                        CollectionFields = objectType.CollectionFields
                            .Select(field => field with
                            {
                                HostTypeName = graphQlTypeName,
                                ElementGraphQlTypeName = TryGetMappedGraphQlTypeName(nameMapByTypeName, field.ElementTypeName, field.ElementGraphQlTypeName),
                                FlatPaths = field.FlatPaths
                                    .Select(path => path with
                                    {
                                        TerminalGraphQlTypeName = TryGetMappedGraphQlTypeName(nameMapByTypeName, path.TerminalTypeName, path.TerminalGraphQlTypeName)
                                    })
                                    .ToImmutableArray()
                            })
                            .ToImmutableArray()
                    };
                })
                .ToImmutableArray();
        }

        private static string TryGetMappedGraphQlTypeName(
            IReadOnlyDictionary<string, string> nameMapByTypeName,
            string typeName,
            string fallbackName) =>
            nameMapByTypeName.TryGetValue(typeName, out var graphQlTypeName)
                ? graphQlTypeName
                : fallbackName;

        private static IReadOnlyDictionary<INamedTypeSymbol, string> BuildUniqueGraphQlTypeNameMap(
            IReadOnlyDictionary<INamedTypeSymbol, ObjectTypeInfo> discoveredTypes)
        {
            var candidateMap = discoveredTypes
                .Where(candidate => !candidate.Value.IsQueryRoot)
                .ToDictionary(
                    candidate => candidate.Key,
                    candidate => GetGraphQlTypeNameCandidates(candidate.Key).ToArray(),
                    SymbolEqualityComparer.Default);
            var assignedNames = new HashSet<string>(StringComparer.Ordinal);
            var nameMap = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);

            foreach (var pair in discoveredTypes
                         .Where(candidate => candidate.Value.IsQueryRoot)
                         .OrderBy(candidate => candidate.Key.ToDisplayString(FullyQualifiedTypeFormat), StringComparer.Ordinal))
            {
                nameMap[pair.Key] = "Query";
                assignedNames.Add("Query");
            }

            foreach (var pair in discoveredTypes
                         .Where(candidate => !candidate.Value.IsQueryRoot)
                         .OrderBy(candidate => candidate.Key.ToDisplayString(FullyQualifiedTypeFormat), StringComparer.Ordinal))
            {
                foreach (var candidateName in candidateMap[pair.Key])
                {
                    if (assignedNames.Contains(candidateName))
                    {
                        continue;
                    }

                    var isUniqueCandidate = candidateMap
                        .Where(candidate => !SymbolEqualityComparer.Default.Equals(candidate.Key, pair.Key))
                        .All(candidate => !candidate.Value.Contains(candidateName, StringComparer.Ordinal));

                    if (isUniqueCandidate)
                    {
                        nameMap[pair.Key] = candidateName;
                        assignedNames.Add(candidateName);
                        goto NextCandidate;
                    }
                }

                if (!nameMap.ContainsKey(pair.Key))
                {
                    var suffix = 2;
                    while (true)
                    {
                        var candidateName = GetGraphQlTypeName(pair.Key) + suffix.ToString();
                        if (assignedNames.Add(candidateName))
                        {
                            nameMap[pair.Key] = candidateName;
                            break;
                        }

                        suffix++;
                    }
                }

            NextCandidate:
                continue;
            }

            return nameMap;
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
                    IsEnhancementObjectType(elementNamedType))
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

                if (propertyType is INamedTypeSymbol objectNamedType && IsEnhancementObjectType(objectNamedType))
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

            return ToCamelCase(string.Concat(target.MemberSegments.Select((member, index) =>
            {
                var fieldName = GetFieldName(member);
                if (index == target.MemberSegments.Length - 1)
                {
                    fieldName = Singularize(fieldName);
                }

                return ToPascalCase(fieldName);
            })));
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
                IMethodSymbol method => UnwrapTaskLike(method.ReturnType),
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

        private static bool IsEnhancementObjectType(ITypeSymbol type)
        {
            var actualType = UnwrapTaskLike(type);
            return actualType is INamedTypeSymbol namedType
                && !IsScalar(namedType)
                && !TryGetCollectionElementType(namedType, out _)
                && !IsNonGenericTaskLike(namedType)
                && namedType is { IsAbstract: false }
                && namedType.SpecialType == SpecialType.None
                && namedType.Name != "Object"
                && namedType.ContainingNamespace is not null
                && IsApplicationType(namedType);
        }

        private static bool IsObjectType(INamedTypeSymbol type) => IsEnhancementObjectType(type);

        private static bool IsApplicationType(INamedTypeSymbol type)
        {
            var assemblyName = type.ContainingAssembly?.Name ?? string.Empty;
            if (assemblyName.StartsWith("HotChocolate.Extension.CollectionEnhancements", StringComparison.Ordinal))
            {
                return true;
            }

            return !assemblyName.StartsWith("System", StringComparison.Ordinal)
                && !assemblyName.StartsWith("Microsoft", StringComparison.Ordinal)
                && !assemblyName.StartsWith("HotChocolate", StringComparison.Ordinal)
                && !assemblyName.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                && !assemblyName.StartsWith("coverlet", StringComparison.OrdinalIgnoreCase);
        }

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
                IsNamedType(namedType.ConstructedFrom, "System.Linq", "IQueryable"))
            {
                elementType = namedType.TypeArguments[0];
                return true;
            }

            var enumerableInterface = type.AllInterfaces.FirstOrDefault(candidate =>
                candidate is INamedTypeSymbol { IsGenericType: true } interfaceType &&
                IsNamedType(interfaceType.ConstructedFrom, "System.Collections.Generic", "IEnumerable"));

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

        private static ITypeSymbol UnwrapTaskLike(ITypeSymbol type)
        {
            var actualType = UnwrapNullable(type);
            return actualType switch
            {
                INamedTypeSymbol { IsGenericType: true } namedType
                    when IsNamedType(namedType.ConstructedFrom, "System.Threading.Tasks", "Task")
                        || IsNamedType(namedType.ConstructedFrom, "System.Threading.Tasks", "ValueTask") =>
                    namedType.TypeArguments[0],
                _ => actualType
            };
        }

        private static bool IsNonGenericTaskLike(ITypeSymbol type) =>
            type is INamedTypeSymbol namedType
            && ((namedType.Name == "Task" || namedType.Name == "ValueTask")
                && namedType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks");

        private static bool IsNamedType(INamedTypeSymbol type, string @namespace, string name) =>
            type.Name == name
            && type.ContainingNamespace.ToDisplayString() == @namespace;

        private static string GetFieldName(ISymbol member) =>
            member switch
            {
                IMethodSymbol method => ToCamelCase(TrimGetPrefix(method.Name)),
                IPropertySymbol property => ToCamelCase(property.Name),
                _ => ToCamelCase(member.Name)
            };

        private static string GetGraphQlTypeName(ITypeSymbol type) =>
            type switch
            {
                IArrayTypeSymbol arrayType => GetGraphQlTypeName(arrayType.ElementType) + "Array",
                INamedTypeSymbol namedType => GetGraphQlTypeName(namedType),
                _ => type.Name
            };

        private static string GetGraphQlTypeName(INamedTypeSymbol type)
        {
            var builder = new StringBuilder();
            AppendGraphQlTypeName(builder, type);
            return builder.ToString();
        }

        private static IEnumerable<string> GetGraphQlTypeNameCandidates(INamedTypeSymbol type)
        {
            var baseName = GetGraphQlTypeName(type);
            yield return baseName;

            var prefix = string.Empty;
            foreach (var segment in GetNamespaceSegments(type.ContainingNamespace).Reverse())
            {
                var sanitizedSegment = SanitizeTypeNameSegment(segment);
                if (string.IsNullOrEmpty(sanitizedSegment))
                {
                    continue;
                }

                prefix = sanitizedSegment + prefix;
                yield return prefix + baseName;
            }

            var assemblySegment = SanitizeTypeNameSegment(type.ContainingAssembly?.Name ?? string.Empty);
            if (!string.IsNullOrEmpty(assemblySegment))
            {
                yield return assemblySegment + baseName;
            }
        }

        private static void AppendGraphQlTypeName(StringBuilder builder, INamedTypeSymbol type)
        {
            if (type.ContainingType is not null)
            {
                AppendGraphQlTypeName(builder, type.ContainingType);
            }

            builder.Append(type.MetadataName.Split('`')[0]);

            foreach (var typeArgument in type.TypeArguments)
            {
                builder.Append(GetGraphQlTypeName(typeArgument));
            }
        }

        private static IEnumerable<string> GetNamespaceSegments(INamespaceSymbol namespaceSymbol)
        {
            var segments = new Stack<string>();
            var current = namespaceSymbol;

            while (current is not null && !current.IsGlobalNamespace)
            {
                segments.Push(current.Name);
                current = current.ContainingNamespace;
            }

            return segments;
        }

        private static string SanitizeTypeNameSegment(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
            }

            if (builder.Length == 0)
            {
                return string.Empty;
            }

            var sanitized = builder.ToString();
            sanitized = char.ToUpperInvariant(sanitized[0]) + sanitized.Substring(1);

            return char.IsDigit(sanitized[0])
                ? "N" + sanitized
                : sanitized;
        }

        private static string GetTypeName(ITypeSymbol type) =>
            UnwrapTaskLike(type).ToDisplayString(FullyQualifiedTypeFormat);

        private static string GetFlatRowPropertyTypeName(ITypeSymbol type)
        {
            var actualType = UnwrapTaskLike(type);
            var typeName = GetTypeName(actualType);
            return actualType.IsReferenceType
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
