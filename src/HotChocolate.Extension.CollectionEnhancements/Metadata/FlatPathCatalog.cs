using System.Reflection;

namespace HotChocolate.Extension.CollectionEnhancements.Metadata;

internal static class FlatPathCatalog
{
    public static IReadOnlyList<FlatPathModel> Discover(CollectionFieldModel collectionField)
    {
        var paths = new List<(string Path, List<MemberInfo> Segments, Type TerminalType)>();
        Discover(collectionField.ElementType, [], [], paths, new HashSet<Type>());

        var groupedByTerminalSegment = paths
            .GroupBy(path => GraphQlNaming.Singularize(GraphQlNaming.GetFieldName(path.Segments[^1])))
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        return paths
            .Select(path =>
            {
                var terminalSegment = GraphQlNaming.Singularize(GraphQlNaming.GetFieldName(path.Segments[^1]));
                var prefix = groupedByTerminalSegment[terminalSegment].Count == 1
                    ? terminalSegment
                    : GetMinimalUniquePrefix(path, groupedByTerminalSegment[terminalSegment]);

                return new FlatPathModel(
                    path.Path,
                    prefix,
                    collectionField,
                    path.Segments,
                    path.TerminalType,
                    GraphQlNaming.GetTypeName(path.TerminalType));
            })
            .OrderBy(path => path.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void Discover(
        Type currentType,
        List<string> pathSegments,
        List<MemberInfo> memberSegments,
        List<(string Path, List<MemberInfo> Segments, Type TerminalType)> paths,
        ISet<Type> visitedTypes)
    {
        if (!visitedTypes.Add(currentType))
        {
            return;
        }

        foreach (var property in currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead))
        {
            if (TypeInspection.IsCollectionType(property.PropertyType, out var elementType) &&
                elementType is not null &&
                TypeInspection.IsEnhancementObjectType(elementType))
            {
                var nextPathSegments = new List<string>(pathSegments) { GraphQlNaming.GetFieldName(property) };
                var nextMemberSegments = new List<MemberInfo>(memberSegments) { property };

                paths.Add((string.Join('.', nextPathSegments), nextMemberSegments, elementType));
                Discover(elementType, nextPathSegments, nextMemberSegments, paths, visitedTypes);
                continue;
            }

            if (TypeInspection.IsEnhancementObjectType(property.PropertyType))
            {
                var nextPathSegments = new List<string>(pathSegments) { GraphQlNaming.GetFieldName(property) };
                var nextMemberSegments = new List<MemberInfo>(memberSegments) { property };
                Discover(property.PropertyType, nextPathSegments, nextMemberSegments, paths, visitedTypes);
            }
        }

        visitedTypes.Remove(currentType);
    }

    private static string GetMinimalUniquePrefix(
        (string Path, List<MemberInfo> Segments, Type TerminalType) target,
        IReadOnlyList<(string Path, List<MemberInfo> Segments, Type TerminalType)> competingPaths)
    {
        var names = target.Segments
            .Select(member => GraphQlNaming.Singularize(GraphQlNaming.GetFieldName(member)))
            .ToArray();

        for (var length = 1; length <= names.Length; length++)
        {
            var prefix = string.Concat(names[^length..].Select(GraphQlNaming.ToPascalCase));
            var normalizedPrefix = GraphQlNaming.ToCamelCase(prefix);

            var duplicates = competingPaths.Count(path =>
                GraphQlNaming.ToCamelCase(
                    string.Concat(path.Segments
                        .Select(member => GraphQlNaming.Singularize(GraphQlNaming.GetFieldName(member)))
                        .ToArray()[^length..]
                        .Select(GraphQlNaming.ToPascalCase))) == normalizedPrefix);

            if (duplicates == 1)
            {
                return normalizedPrefix;
            }
        }

        return GraphQlNaming.ToCamelCase(string.Concat(
            target.Segments.Select((member, index) =>
            {
                var fieldName = GraphQlNaming.GetFieldName(member);
                if (index == target.Segments.Count - 1)
                {
                    fieldName = GraphQlNaming.Singularize(fieldName);
                }

                return GraphQlNaming.ToPascalCase(fieldName);
            })));
    }
}
