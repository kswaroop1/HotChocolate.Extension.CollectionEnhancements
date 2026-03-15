using System.Collections.Concurrent;
using HotChocolate.Extension.CollectionEnhancements.Metadata;

namespace HotChocolate.Extension.CollectionEnhancements.Execution;

internal sealed class FlatRowShape(
    CollectionFieldModel collectionField,
    IReadOnlyList<FlatPathModel> paths,
    IReadOnlyDictionary<string, FlatPathModel> generatedFieldOwners)
{
    public CollectionFieldModel CollectionField { get; } = collectionField;

    public IReadOnlyList<FlatPathModel> Paths { get; } = paths;

    public IReadOnlyDictionary<string, FlatPathModel> GeneratedFieldOwners { get; } = generatedFieldOwners;
}

internal static class FlatRowShapeCache
{
    private static readonly ConcurrentDictionary<CollectionFieldModel, FlatRowShape> Cache = [];

    public static FlatRowShape GetOrCreate(CollectionFieldModel collectionField) =>
        Cache.GetOrAdd(collectionField, Create);

    private static FlatRowShape Create(CollectionFieldModel collectionField)
    {
        var paths = FlatPathCatalog.Discover(collectionField);
        var generatedFieldOwners = new Dictionary<string, FlatPathModel>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            foreach (var property in path.TerminalElementType.GetProperties().Where(p => p.CanRead && TypeInspection.IsScalar(p.PropertyType)))
            {
                var fieldName = path.Prefix + GraphQlNaming.ToPascalCase(GraphQlNaming.GetFieldName(property));
                generatedFieldOwners[fieldName] = path;
            }
        }

        return new FlatRowShape(collectionField, paths, generatedFieldOwners);
    }
}
