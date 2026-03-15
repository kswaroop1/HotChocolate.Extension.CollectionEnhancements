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

    public static bool IsScalar(Type type)
    {
        var actualType = UnwrapNullable(type);
        return actualType.IsEnum || SimpleTypes.Contains(actualType);
    }

    public static bool IsNumeric(Type type)
    {
        var actualType = UnwrapNullable(type);

        return actualType == typeof(byte)
            || actualType == typeof(short)
            || actualType == typeof(int)
            || actualType == typeof(long)
            || actualType == typeof(float)
            || actualType == typeof(double)
            || actualType == typeof(decimal);
    }

    public static bool IsStringLike(Type type) => UnwrapNullable(type) == typeof(string);

    public static bool IsCollectionType(Type type, out Type? elementType)
    {
        if (type == typeof(string))
        {
            elementType = null;
            return false;
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType();
            return elementType is not null;
        }

        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(IQueryable<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        var enumerableInterface = type
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
