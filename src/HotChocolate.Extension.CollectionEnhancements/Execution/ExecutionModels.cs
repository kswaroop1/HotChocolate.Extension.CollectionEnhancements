using HotChocolate.Extension.CollectionEnhancements.Metadata;

namespace HotChocolate.Extension.CollectionEnhancements.Execution;

internal enum AggregateOperator
{
    CountDistinct,
    Sum,
    Avg,
    Min,
    Max,
    Stdev,
    Stdevp,
    Skew,
    Kurtosis,
    StringAgg,
    StringAggDistinct
}

internal sealed class AggregateSelectionContext
{
    private readonly Func<IReadOnlyList<object>> _rowsFactory;
    private readonly Dictionary<string, object?> _aggregateCache = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();
    private IReadOnlyList<object>? _rows;

    public AggregateSelectionContext(
        CollectionFieldModel collectionField,
        bool isFlat,
        IReadOnlyList<object> rows)
    {
        CollectionField = collectionField;
        IsFlat = isFlat;
        _rows = rows;
        _rowsFactory = static () => [];
    }

    public AggregateSelectionContext(
        CollectionFieldModel collectionField,
        bool isFlat,
        IQueryable queryable)
    {
        CollectionField = collectionField;
        IsFlat = isFlat;
        QueryableSource = queryable;
        _rowsFactory = () => queryable.Cast<object>().ToArray();
    }

    public CollectionFieldModel CollectionField { get; }

    public bool IsFlat { get; }

    public IQueryable? QueryableSource { get; }

    public IReadOnlyList<object> Rows
    {
        get
        {
            lock (_sync)
            {
                _rows ??= _rowsFactory();
                return _rows;
            }
        }
    }

    public string ResultPrefix => IsFlat ? "Flat" : string.Empty;

    public bool TryGetCachedAggregate(string key, out object? value)
    {
        lock (_sync)
        {
            return _aggregateCache.TryGetValue(key, out value);
        }
    }

    public object? GetOrAddCachedAggregate(string key, Func<object?> factory)
    {
        lock (_sync)
        {
            if (_aggregateCache.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var value = factory();
            _aggregateCache[key] = value;
            return value;
        }
    }
}

internal sealed class AggregateProjection(
    AggregateSelectionContext selection,
    AggregateOperator @operator,
    string? separator,
    IReadOnlyList<(string FieldName, bool Descending)>? order)
{
    public AggregateSelectionContext Selection { get; } = selection;

    public AggregateOperator Operator { get; } = @operator;

    public string? Separator { get; } = separator;

    public IReadOnlyList<(string FieldName, bool Descending)>? Order { get; } = order;
}

internal sealed class GroupRowResult(
    IReadOnlyDictionary<string, object?> key,
    AggregateSelectionContext selection)
{
    public IReadOnlyDictionary<string, object?> Key { get; } = key;

    public AggregateSelectionContext Selection { get; } = selection;
}
