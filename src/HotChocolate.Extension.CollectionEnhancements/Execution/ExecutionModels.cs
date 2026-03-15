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

internal sealed class AggregateSelectionContext(
    CollectionFieldModel collectionField,
    bool isFlat,
    IReadOnlyList<object> rows)
{
    public CollectionFieldModel CollectionField { get; } = collectionField;

    public bool IsFlat { get; } = isFlat;

    public IReadOnlyList<object> Rows { get; } = rows;

    public string ResultPrefix => IsFlat ? "Flat" : string.Empty;
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
