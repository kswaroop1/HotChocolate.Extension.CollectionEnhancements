using System.Globalization;
using System.Reflection;
using System.Text;

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

    public static string GetTypeName(Type type)
    {
        var actualType = TypeInspection.UnwrapTaskLike(type);

        if (actualType == typeof(void))
        {
            return "Void";
        }

        if (actualType.IsArray)
        {
            return GetTypeName(actualType.GetElementType()!) + "Array";
        }

        if (actualType.IsGenericParameter)
        {
            return actualType.Name;
        }

        var chain = new Stack<Type>();
        for (var current = actualType; current is not null; current = current.DeclaringType)
        {
            chain.Push(current);
        }

        var genericArguments = actualType.IsGenericType
            ? actualType.GetGenericArguments()
            : Type.EmptyTypes;
        var argumentIndex = 0;
        var builder = new StringBuilder();

        while (chain.Count > 0)
        {
            var segment = chain.Pop();
            var name = segment.Name;
            var tickIndex = name.IndexOf('`');
            var localArity = 0;

            if (tickIndex >= 0)
            {
                localArity = int.Parse(name[(tickIndex + 1)..], CultureInfo.InvariantCulture);
                name = name[..tickIndex];
            }

            builder.Append(name);

            for (var index = 0; index < localArity && argumentIndex < genericArguments.Length; index++)
            {
                builder.Append(GetTypeName(genericArguments[argumentIndex++]));
            }
        }

        return builder.ToString();
    }

    public static IReadOnlyList<string> GetTypeNameCandidates(Type type)
    {
        var actualType = TypeInspection.UnwrapTaskLike(type);
        var baseName = GetTypeName(actualType);
        var candidates = new List<string> { baseName };
        var prefix = string.Empty;

        foreach (var sanitizedSegment in (actualType.Namespace ?? string.Empty)
                     .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Reverse()
                     .Select(SanitizeTypeNameSegment)
                     .Where(segment => !string.IsNullOrEmpty(segment)))
        {
            prefix = sanitizedSegment + prefix;
            candidates.Add(prefix + baseName);
        }

        var assemblySegment = SanitizeTypeNameSegment(actualType.Assembly.GetName().Name!);
        if (!string.IsNullOrEmpty(assemblySegment))
        {
            candidates.Add(assemblySegment + baseName);
        }

        return candidates
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

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
        sanitized = char.ToUpperInvariant(sanitized[0]) + sanitized[1..];

        return char.IsDigit(sanitized[0])
            ? "N" + sanitized
            : sanitized;
    }

}
