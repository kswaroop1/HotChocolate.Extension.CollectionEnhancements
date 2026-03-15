using System.Reflection;

namespace HotChocolate.Extension.CollectionEnhancements.Metadata;

internal static class GraphQlNaming
{
    public static string GetFieldName(MemberInfo member) =>
        member switch
        {
            MethodInfo method => ToCamelCase(TrimGetPrefix(method.Name)),
            PropertyInfo property => ToCamelCase(property.Name),
            _ => ToCamelCase(member.Name)
        };

    public static string GetTypeName(Type type) => type.Name;

    public static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length == 1
            ? value.ToLowerInvariant()
            : char.ToLowerInvariant(value[0]) + value[1..];
    }

    public static string ToPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length == 1
            ? value.ToUpperInvariant()
            : char.ToUpperInvariant(value[0]) + value[1..];
    }

    public static string Singularize(string value)
    {
        if (value.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
        {
            return value[..^3] + "y";
        }

        if (value.EndsWith("ses", StringComparison.OrdinalIgnoreCase))
        {
            return value[..^2];
        }

        if (value.EndsWith('s') && value.Length > 1)
        {
            return value[..^1];
        }

        return value;
    }

    private static string TrimGetPrefix(string value) =>
        value.StartsWith("Get", StringComparison.Ordinal) && value.Length > 3
            ? value[3..]
            : value;
}
