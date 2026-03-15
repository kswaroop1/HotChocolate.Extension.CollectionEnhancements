using System.Reflection;

namespace HotChocolate.Extension.CollectionEnhancements.Metadata;

internal sealed class CollectionSchemaCatalog
{
    private static readonly HashSet<string> SupportedQueryParameterAttributes =
    [
        "ServiceAttribute",
        "ParentAttribute"
    ];

    private readonly Dictionary<Type, ObjectTypeModel> _models = [];
    private readonly Lock _sync = new();

    public IReadOnlyCollection<ObjectTypeModel> ObjectTypes => _models.Values;

    public static CollectionSchemaCatalog CreateDefault()
    {
        var catalog = new CollectionSchemaCatalog();
        catalog.DiscoverFromLoadedAssemblies();
        return catalog;
    }

    public ObjectTypeModel? TryGetObjectType(Type clrType)
    {
        lock (_sync)
        {
            if (_models.TryGetValue(clrType, out var existing))
            {
                return existing;
            }

            if (clrType.IsAbstract
                || clrType == typeof(object)
                || clrType.Namespace is null
                || !IsApplicationAssembly(clrType.Assembly))
            {
                return null;
            }

            var model = GetOrCreateModel(clrType, isQueryRoot: IsQueryType(clrType));
            PopulateModel(model);
            return model;
        }
    }

    private void DiscoverFromLoadedAssemblies()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsApplicationAssembly)
            .ToArray();

        foreach (var queryType in assemblies.SelectMany(a => SafeGetTypes(a)).Where(IsQueryType))
        {
            GetOrCreateModel(queryType, isQueryRoot: true);
        }

        foreach (var model in _models.Values.ToArray())
        {
            PopulateModel(model);
        }
    }

    private ObjectTypeModel GetOrCreateModel(Type clrType, bool isQueryRoot = false)
    {
        if (_models.TryGetValue(clrType, out var existing))
        {
            return existing;
        }

        var graphQlTypeName = isQueryRoot ? "Query" : GraphQlNaming.GetTypeName(clrType);
        var model = new ObjectTypeModel(clrType, graphQlTypeName, isQueryRoot);
        _models.Add(clrType, model);
        return model;
    }

    private void PopulateModel(ObjectTypeModel model)
    {
        if (model.ScalarFields.Count > 0 || model.ObjectFields.Count > 0 || model.CollectionFields.Count > 0)
        {
            return;
        }

        var members = model.IsQueryRoot
            ? GetQueryMembers(model.ClrType)
            : GetObjectMembers(model.ClrType);

        foreach (var member in members)
        {
            var memberType = GetMemberType(member);
            var graphQlName = GraphQlNaming.GetFieldName(member);

            if (TypeInspection.IsCollectionType(memberType, out var elementType) &&
                elementType is not null &&
                !TypeInspection.IsScalar(elementType))
            {
                var collectionField = new CollectionFieldModel(
                    member,
                    graphQlName,
                    memberType,
                    elementType,
                    model.GraphQlTypeName,
                    GraphQlNaming.GetTypeName(elementType));

                model.CollectionFields.Add(collectionField);
                PopulateModel(GetOrCreateModel(elementType));
                continue;
            }

            if (TypeInspection.IsScalar(memberType))
            {
                model.ScalarFields.Add(new ScalarFieldModel(member, graphQlName, memberType));
                continue;
            }

            if (!memberType.IsAbstract &&
                memberType != typeof(object) &&
                memberType.Namespace is not null)
            {
                model.ObjectFields.Add(new ObjectReferenceFieldModel(member, graphQlName, memberType));
                PopulateModel(GetOrCreateModel(memberType));
            }
        }
    }

    private static IEnumerable<MemberInfo> GetQueryMembers(Type queryType) =>
        queryType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.GetParameters().All(IsSupportedQueryParameter));

    private static bool IsSupportedQueryParameter(ParameterInfo parameter) =>
        parameter.GetCustomAttributes()
            .Any(attribute => SupportedQueryParameterAttributes.Contains(attribute.GetType().Name));

    private static IEnumerable<MemberInfo> GetObjectMembers(Type clrType) =>
        clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead);

    private static Type GetMemberType(MemberInfo member) =>
        member switch
        {
            PropertyInfo property => property.PropertyType,
            MethodInfo method => method.ReturnType,
            _ => throw new NotSupportedException($"Unsupported member {member}.")
        };

    private static bool IsQueryType(Type type) =>
        type is { IsClass: true, IsAbstract: false, Name: "Query" };

    private static bool IsApplicationAssembly(Assembly assembly)
    {
        if (assembly.IsDynamic)
        {
            return false;
        }

        var name = assembly.GetName().Name ?? string.Empty;

        if (name.StartsWith("HotChocolate.Extension.CollectionEnhancements", StringComparison.Ordinal))
        {
            return true;
        }

        return !name.StartsWith("System", StringComparison.Ordinal)
            && !name.StartsWith("Microsoft", StringComparison.Ordinal)
            && !name.StartsWith("HotChocolate", StringComparison.Ordinal)
            && !name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("coverlet", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
