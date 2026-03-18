namespace HotChocolate.Extension.CollectionEnhancements.Metadata;

internal static class TypeInspection
{
    private static readonly HashSet<Type> SimpleTypes =
    [
        typeof(bool),
        typeof(byte),
        typeof(short),
        typeof(int),
        typeof(long),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(string),
        typeof(DateOnly),
        typeof(DateTime),
        typeof(Guid)
    ];

    public static bool IsNullable(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    public static Type UnwrapNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    public static Type UnwrapTaskLike(Type type)
    {
        var actualType = UnwrapNullable(type);

        if (actualType == typeof(Task) || actualType == typeof(ValueTask))
        {
            return typeof(void);
        }

        if (actualType.IsGenericType)
        {
            var genericTypeDefinition = actualType.GetGenericTypeDefinition();
            if (genericTypeDefinition == typeof(Task<>) || genericTypeDefinition == typeof(ValueTask<>))
            {
                return actualType.GetGenericArguments()[0];
            }
        }

        return actualType;
    }

    public static bool IsScalar(Type type)
    {
        var actualType = UnwrapTaskLike(type);
        return actualType.IsEnum || SimpleTypes.Contains(actualType);
    }

    public static bool IsNumeric(Type type)
    {
        var actualType = UnwrapTaskLike(type);

        return actualType == typeof(byte)
            || actualType == typeof(short)
            || actualType == typeof(int)
            || actualType == typeof(long)
            || actualType == typeof(float)
            || actualType == typeof(double)
            || actualType == typeof(decimal);
    }

    public static bool IsStringLike(Type type) => UnwrapTaskLike(type) == typeof(string);

    public static bool IsEnhancementObjectType(Type type)
    {
        var actualType = UnwrapTaskLike(type);
        return !IsScalar(actualType)
            && !IsCollectionType(actualType, out _)
            && !actualType.IsAbstract
            && actualType != typeof(object)
            && actualType != typeof(void)
            && actualType.Namespace is not null;
    }

    public static bool IsCollectionType(Type type, out Type? elementType)
    {
        var actualType = UnwrapTaskLike(type);

        if (actualType == typeof(string) || actualType == typeof(void))
        {
            elementType = null;
            return false;
        }

        if (actualType.IsArray)
        {
            elementType = actualType.GetElementType();
            return elementType is not null;
        }

        if (actualType.IsGenericType &&
            actualType.GetGenericTypeDefinition() == typeof(IQueryable<>))
        {
            elementType = actualType.GetGenericArguments()[0];
            return true;
        }

        var enumerableInterface = actualType
            .GetInterfaces()
            .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        if (enumerableInterface is not null)
        {
            elementType = enumerableInterface.GetGenericArguments()[0];
            return true;
        }

        elementType = null;
        return false;
    }
}
