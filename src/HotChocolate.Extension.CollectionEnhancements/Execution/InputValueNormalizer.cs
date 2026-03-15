using System.Collections;

namespace HotChocolate.Extension.CollectionEnhancements.Execution;

internal static class InputValueNormalizer
{
    public static object? Normalize(object? value) =>
        value switch
        {
            null => null,
            IReadOnlyDictionary<string, object?> readOnlyDictionary => NormalizeReadOnlyDictionary(readOnlyDictionary),
            IDictionary dictionary => NormalizeDictionary(dictionary),
            IReadOnlyList<object?> readOnlyList => NormalizeReadOnlyList(readOnlyList),
            IEnumerable enumerable when value is not string => NormalizeList(enumerable),
            _ => value
        };

    public static IReadOnlyDictionary<string, object?> AsDictionary(object? value) =>
        Normalize(value) as IReadOnlyDictionary<string, object?>
        ?? new Dictionary<string, object?>(StringComparer.Ordinal);

    public static IReadOnlyList<object?> AsList(object? value) =>
        Normalize(value) as IReadOnlyList<object?>
        ?? [];

    public static bool IsEmpty(object? value) =>
        Normalize(value) switch
        {
            null => true,
            IReadOnlyDictionary<string, object?> dictionary => dictionary.Count == 0 || dictionary.All(pair => IsEmpty(pair.Value)),
            IReadOnlyList<object?> list => list.Count == 0 || list.All(IsEmpty),
            _ => false
        };

    private static IReadOnlyDictionary<string, object?> NormalizeDictionary(IDictionary dictionary)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not null)
            {
                normalized[entry.Key.ToString()!] = Normalize(entry.Value);
            }
        }

        return normalized;
    }

    private static IReadOnlyList<object?> NormalizeList(IEnumerable enumerable)
    {
        var normalized = new List<object?>();

        foreach (var item in enumerable)
        {
            normalized.Add(Normalize(item));
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, object?> NormalizeReadOnlyDictionary(IReadOnlyDictionary<string, object?> dictionary)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (key, value) in dictionary)
        {
            normalized[key] = Normalize(value);
        }

        return normalized;
    }

    private static IReadOnlyList<object?> NormalizeReadOnlyList(IReadOnlyList<object?> list)
    {
        var normalized = new List<object?>(list.Count);

        foreach (var item in list)
        {
            normalized.Add(Normalize(item));
        }

        return normalized;
    }
}
