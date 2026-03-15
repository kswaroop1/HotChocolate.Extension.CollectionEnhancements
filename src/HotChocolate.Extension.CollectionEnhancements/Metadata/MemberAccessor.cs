using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace HotChocolate.Extension.CollectionEnhancements.Metadata;

internal static class MemberAccessor
{
    private static readonly ConcurrentDictionary<MemberInfo, Func<object, object?>> Getters = [];

    public static object? GetValue(MemberInfo member, object source) =>
        Getters.GetOrAdd(member, CreateGetter)(source);

    private static Func<object, object?> CreateGetter(MemberInfo member)
    {
        var source = Expression.Parameter(typeof(object), "source");
        var typedSource = Expression.Convert(source, member.DeclaringType!);

        Expression access = member switch
        {
            PropertyInfo property => Expression.Property(typedSource, property),
            FieldInfo field => Expression.Field(typedSource, field),
            MethodInfo method => Expression.Call(typedSource, method),
            _ => throw new NotSupportedException($"Unsupported member {member}.")
        };

        var cast = Expression.Convert(access, typeof(object));
        return Expression.Lambda<Func<object, object?>>(cast, source).Compile();
    }
}
