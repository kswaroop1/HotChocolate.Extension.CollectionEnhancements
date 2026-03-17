using HotChocolate.Extension.CollectionEnhancements.Metadata;

namespace HotChocolate.Extension.CollectionEnhancements.Execution;

internal sealed partial class CollectionExecutionEngine(
    CollectionSchemaCatalog catalog,
    CollectionEnhancementOptions? options = null)
{
    private readonly CollectionSchemaCatalog _catalog = catalog;
    private readonly CollectionEnhancementOptions _options = options ?? new();

    public IReadOnlyList<object> ApplyCollectionArguments(
        CollectionFieldModel collectionField,
        object? sourceValue,
        object? where,
        object? order,
        int? offset,
        int? limit)
    {
        var rows = ToObjectRows(sourceValue);
        var filtered = ApplyObjectFilter(collectionField.ElementType, rows, where);
        var ordered = ApplyRowSort(collectionField.ElementType, filtered, order);
        return ApplyWindow(ordered, offset, limit);
    }

    public AggregateSelectionContext? CreateAggregateSelection(
        CollectionFieldModel collectionField,
        object? sourceValue,
        object? where,
        object? having,
        bool isFlat,
        IReadOnlyList<string>? expand)
    {
        if (!isFlat &&
            TryCreateQueryableAggregateSelection(collectionField, sourceValue, where, out var queryableSelection))
        {
            return PassesHaving(queryableSelection, having)
                ? queryableSelection
                : null;
        }

        var rows = isFlat
            ? ApplyFlatArguments(collectionField, sourceValue, expand ?? [], where, order: null, offset: null, limit: null)
            : ApplyCollectionArguments(collectionField, sourceValue, where, order: null, offset: null, limit: null);

        var selection = new AggregateSelectionContext(collectionField, isFlat, rows);
        return PassesHaving(selection, having) ? selection : null;
    }

    public object CreateGroupRowsForField(
        CollectionFieldModel collectionField,
        object? sourceValue,
        object? by,
        object? where,
        object? having,
        object? order,
        int? offset,
        int? limit,
        bool isFlat,
        IReadOnlyList<string>? expand) =>
        WrapAsExecutable(
            typeof(GroupRowResult),
            CreateGroupRows(
                collectionField,
                sourceValue,
                by,
                where,
                having,
                order,
                offset,
                limit,
                isFlat,
                expand));

    public IReadOnlyList<GroupRowResult> CreateGroupRows(
        CollectionFieldModel collectionField,
        object? sourceValue,
        object? by,
        object? where,
        object? having,
        object? order,
        int? offset,
        int? limit,
        bool isFlat,
        IReadOnlyList<string>? expand)
    {
        var groupByFields = InputValueNormalizer.AsList(by)
            .Select(value => value?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        if (groupByFields.Length == 0)
        {
            return [];
        }

        if (!isFlat &&
            TryCreateQueryableGroupRows(collectionField, sourceValue, groupByFields, where, out var queryableGroups))
        {
            var filteredQueryableGroups = queryableGroups
                .Where(group => PassesHaving(group.Selection, having))
                .ToArray();

            var orderedQueryableGroups = ApplyGroupSort(filteredQueryableGroups, order);
            return ApplyGroupWindow(orderedQueryableGroups, offset, limit);
        }

        var rows = isFlat
            ? ApplyFlatArguments(collectionField, sourceValue, expand ?? [], where, order: null, offset: null, limit: null)
            : ApplyCollectionArguments(collectionField, sourceValue, where, order: null, offset: null, limit: null);

        var groupedRows = rows
            .GroupBy(row => BuildGroupKey(collectionField, row, groupByFields, isFlat), GroupKeyComparer.Instance)
            .Select(group =>
            {
                var selection = new AggregateSelectionContext(collectionField, isFlat, group.ToArray());
                return new GroupRowResult(group.Key, selection);
            })
            .Where(group => PassesHaving(group.Selection, having))
            .ToArray();

        var ordered = ApplyGroupSort(groupedRows, order);
        return ApplyGroupWindow(ordered, offset, limit);
    }

    public IReadOnlyList<object> ApplyFlatArguments(
        CollectionFieldModel collectionField,
        object? sourceValue,
        IReadOnlyList<string> expand,
        object? where,
        object? order,
        int? offset,
        int? limit)
    {
        var baseRows = ToObjectRows(sourceValue);
        var flatRows = ExpandRows(collectionField, baseRows, expand);
        ValidateFlatReferences(collectionField, expand, where, order);
        var filtered = ApplyFlatFilter(collectionField, flatRows, where);
        var ordered = ApplyFlatSort(filtered, order);
        return ApplyWindow(ordered, offset, limit);
    }

    public object ApplyFlatArgumentsForField(
        CollectionFieldModel collectionField,
        object? sourceValue,
        IReadOnlyList<string> expand,
        object? where,
        object? order,
        int? offset,
        int? limit) =>
        WrapAsExecutable(
            typeof(IReadOnlyDictionary<string, object?>),
            ApplyFlatArguments(
                collectionField,
                sourceValue,
                expand,
                where,
                order,
                offset,
                limit));

    public object? ResolveFieldValue(Type hostType, object row, string fieldName, bool isFlat)
    {
        if (isFlat)
        {
            return ((IReadOnlyDictionary<string, object?>)row).TryGetValue(fieldName, out var value)
                ? value
                : null;
        }

        var model = _catalog.TryGetObjectType(hostType)
            ?? throw new InvalidOperationException($"No object model found for {hostType}.");

        if (model.FindScalar(fieldName) is { } scalar)
        {
            return MemberAccessor.GetValue(scalar.Member, row);
        }

        if (model.FindObject(fieldName) is { } nested)
        {
            return MemberAccessor.GetValue(nested.Member, row);
        }

        return null;
    }

    public int ResolveCount(AggregateSelectionContext selection, object? where)
    {
        if (!selection.IsFlat &&
            selection.QueryableSource is { } queryable)
        {
            if (where is null)
            {
                return ExecuteQueryableCount(queryable);
            }

            if (TryApplyQueryableObjectFilter(selection.CollectionField.ElementType, queryable, where, out var filtered))
            {
                return ExecuteQueryableCount(filtered);
            }
        }

        if (where is null)
        {
            return selection.Rows.Count;
        }

        return selection.IsFlat
            ? ApplyFlatFilter(selection.CollectionField, selection.Rows, where).Count
            : ApplyObjectFilter(selection.CollectionField.ElementType, selection.Rows, where).Count;
    }

    public object? ResolveAggregateProjectionField(AggregateProjection projection, string fieldName)
    {
        var cacheKey = $"{projection.Operator}:{fieldName}:{projection.Separator}:{FormatOrderCacheKey(projection.Order)}";
        return projection.Selection.GetOrAddCachedAggregate(
            cacheKey,
            () => ResolveAggregateProjectionFieldCore(projection, fieldName));
    }

    private object? ResolveAggregateProjectionFieldCore(AggregateProjection projection, string fieldName)
    {
        return projection.Operator switch
        {
            AggregateOperator.CountDistinct => GetDistinctCount(projection.Selection, fieldName),
            AggregateOperator.Sum => GetNumericAggregate(projection.Selection, fieldName, NumericAggregate.Sum),
            AggregateOperator.Avg => GetNumericAggregate(projection.Selection, fieldName, NumericAggregate.Average),
            AggregateOperator.Min => GetMinOrMax(projection.Selection, fieldName, min: true),
            AggregateOperator.Max => GetMinOrMax(projection.Selection, fieldName, min: false),
            AggregateOperator.Stdev => GetNumericAggregate(projection.Selection, fieldName, NumericAggregate.SampleStdev),
            AggregateOperator.Stdevp => GetNumericAggregate(projection.Selection, fieldName, NumericAggregate.PopulationStdev),
            AggregateOperator.Skew => GetNumericAggregate(projection.Selection, fieldName, NumericAggregate.Skew),
            AggregateOperator.Kurtosis => GetNumericAggregate(projection.Selection, fieldName, NumericAggregate.Kurtosis),
            AggregateOperator.StringAgg => GetStringAggregate(projection.Selection, fieldName, projection.Separator ?? ",", projection.Order, distinct: false),
            AggregateOperator.StringAggDistinct => GetStringAggregate(projection.Selection, fieldName, projection.Separator ?? ",", projection.Order, distinct: true),
            _ => null
        };
    }

    public bool ValidateNumericAggregateField(CollectionFieldModel collectionField, string fieldName, bool isFlat)
    {
        if (isFlat)
        {
            var flatShape = FlatRowShapeCache.GetOrCreate(collectionField);

            if (flatShape.GeneratedFieldOwners.TryGetValue(fieldName, out var path))
            {
                return path.TerminalElementType
                    .GetProperties()
                    .First(property => path.Prefix + GraphQlNaming.ToPascalCase(GraphQlNaming.GetFieldName(property)) == fieldName)
                    .PropertyType is var clrType &&
                    TypeInspection.IsNumeric(clrType);
            }

            return IsBaseNumericField(collectionField, fieldName);
        }

        return _catalog.TryGetObjectType(collectionField.ElementType)?.FindScalar(fieldName) is { ClrType: var modelType }
            && TypeInspection.IsNumeric(modelType);
    }

    public bool ValidateStringAggregateField(CollectionFieldModel collectionField, string fieldName, bool isFlat)
    {
        if (isFlat)
        {
            var flatShape = FlatRowShapeCache.GetOrCreate(collectionField);

            if (flatShape.GeneratedFieldOwners.TryGetValue(fieldName, out var path))
            {
                return path.TerminalElementType
                    .GetProperties()
                    .First(property => path.Prefix + GraphQlNaming.ToPascalCase(GraphQlNaming.GetFieldName(property)) == fieldName)
                    .PropertyType is var clrType &&
                    TypeInspection.IsStringLike(clrType);
            }

            return IsBaseStringField(collectionField, fieldName);
        }

        return _catalog.TryGetObjectType(collectionField.ElementType)?.FindScalar(fieldName) is { ClrType: var modelType }
            && TypeInspection.IsStringLike(modelType);
    }

    private bool IsBaseNumericField(CollectionFieldModel collectionField, string fieldName) =>
        _catalog.TryGetObjectType(collectionField.ElementType)?.FindScalar(fieldName) is { ClrType: var clrType }
        && TypeInspection.IsNumeric(clrType);

    private bool IsBaseStringField(CollectionFieldModel collectionField, string fieldName) =>
        _catalog.TryGetObjectType(collectionField.ElementType)?.FindScalar(fieldName) is { ClrType: var clrType }
        && TypeInspection.IsStringLike(clrType);

    private IReadOnlyList<object> ApplyObjectFilter(Type elementType, IReadOnlyList<object> rows, object? where)
    {
        if (where is null)
        {
            return rows;
        }

        var normalizedWhere = InputValueNormalizer.AsDictionary(where);
        return rows.Where(row => MatchesObjectFilter(elementType, row, normalizedWhere)).ToArray();
    }

    private IReadOnlyList<object> ApplyFlatFilter(CollectionFieldModel collectionField, IReadOnlyList<object> rows, object? where)
    {
        if (where is null)
        {
            return rows;
        }

        var normalizedWhere = InputValueNormalizer.AsDictionary(where);
        return rows.Where(row => MatchesFlatFilter(collectionField, (IReadOnlyDictionary<string, object?>)row, normalizedWhere)).ToArray();
    }

    private bool MatchesObjectFilter(Type elementType, object row, IReadOnlyDictionary<string, object?> filter)
    {
        var model = _catalog.TryGetObjectType(elementType)
            ?? throw new InvalidOperationException($"No object model found for {elementType}.");

        foreach (var (name, value) in filter)
        {
            if (InputValueNormalizer.IsEmpty(value))
            {
                continue;
            }

            switch (name)
            {
                case "and":
                    if (!InputValueNormalizer.AsList(value).All(item => MatchesObjectFilter(elementType, row, InputValueNormalizer.AsDictionary(item))))
                    {
                        return false;
                    }

                    continue;

                case "or":
                    if (!InputValueNormalizer.AsList(value).Any(item => MatchesObjectFilter(elementType, row, InputValueNormalizer.AsDictionary(item))))
                    {
                        return false;
                    }

                    continue;

                case "not":
                    if (MatchesObjectFilter(elementType, row, InputValueNormalizer.AsDictionary(value)))
                    {
                        return false;
                    }

                    continue;
            }

            if (model.FindScalar(name) is { } scalar)
            {
                if (!MatchesScalarOperations(MemberAccessor.GetValue(scalar.Member, row), InputValueNormalizer.AsDictionary(value)))
                {
                    return false;
                }

                continue;
            }

            if (model.FindObject(name) is { } objectField)
            {
                var nested = MemberAccessor.GetValue(objectField.Member, row);
                if (nested is null || !MatchesObjectFilter(objectField.ClrType, nested, InputValueNormalizer.AsDictionary(value)))
                {
                    return false;
                }

                continue;
            }

            if (model.CollectionFields.FirstOrDefault(field => field.AggregateFieldName == name) is { } aggregateField)
            {
                var criteria = InputValueNormalizer.AsDictionary(value);
                if (criteria.Count == 0)
                {
                    continue;
                }

                var selection = CreateAggregateSelection(
                    aggregateField,
                    MemberAccessor.GetValue(aggregateField.Member, row),
                    criteria.TryGetValue("where", out var childWhere) ? childWhere : null,
                    criteria.TryGetValue("having", out var childHaving) ? childHaving : null,
                    isFlat: false,
                    expand: null);

                if (selection is null)
                {
                    return false;
                }

                continue;
            }

            if (model.CollectionFields.FirstOrDefault(field => field.GroupFieldName == name) is { } groupField)
            {
                var criteria = InputValueNormalizer.AsDictionary(value);
                if (criteria.Count == 0)
                {
                    continue;
                }

                var groups = CreateGroupRows(
                    groupField,
                    MemberAccessor.GetValue(groupField.Member, row),
                    criteria.TryGetValue("by", out var childBy) ? childBy : null,
                    criteria.TryGetValue("where", out var childWhere) ? childWhere : null,
                    criteria.TryGetValue("having", out var childHaving) ? childHaving : null,
                    criteria.TryGetValue("order", out var childOrder) ? childOrder : null,
                    criteria.TryGetValue("offset", out var childOffset) ? ConvertToNullableInt(childOffset) : null,
                    criteria.TryGetValue("limit", out var childLimit) ? ConvertToNullableInt(childLimit) : null,
                    isFlat: false,
                    expand: null);

                if (groups.Count == 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool MatchesFlatFilter(
        CollectionFieldModel collectionField,
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyDictionary<string, object?> filter)
    {
        var flatShape = FlatRowShapeCache.GetOrCreate(collectionField);

        foreach (var (name, value) in filter)
        {
            if (InputValueNormalizer.IsEmpty(value))
            {
                continue;
            }

            switch (name)
            {
                case "and":
                    if (!InputValueNormalizer.AsList(value).All(item => MatchesFlatFilter(collectionField, row, InputValueNormalizer.AsDictionary(item))))
                    {
                        return false;
                    }

                    continue;

                case "or":
                    if (!InputValueNormalizer.AsList(value).Any(item => MatchesFlatFilter(collectionField, row, InputValueNormalizer.AsDictionary(item))))
                    {
                        return false;
                    }

                    continue;

                case "not":
                    if (MatchesFlatFilter(collectionField, row, InputValueNormalizer.AsDictionary(value)))
                    {
                        return false;
                    }

                    continue;
            }

            if (!row.ContainsKey(name) && !flatShape.GeneratedFieldOwners.ContainsKey(name))
            {
                return false;
            }

            if (!MatchesScalarOperations(row.GetValueOrDefault(name), InputValueNormalizer.AsDictionary(value)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesScalarOperations(object? rowValue, IReadOnlyDictionary<string, object?> operations)
    {
        foreach (var (operation, operand) in operations)
        {
            if (InputValueNormalizer.IsEmpty(operand))
            {
                continue;
            }

            switch (operation)
            {
                case "eq" when !ComparisonHelper.EqualsValue(rowValue, operand):
                case "neq" when ComparisonHelper.EqualsValue(rowValue, operand):
                    return false;

                case "gt" when ComparisonHelper.Compare(rowValue, operand) <= 0:
                case "gte" when ComparisonHelper.Compare(rowValue, operand) < 0:
                case "lt" when ComparisonHelper.Compare(rowValue, operand) >= 0:
                case "lte" when ComparisonHelper.Compare(rowValue, operand) > 0:
                    return false;

                case "in" when !InputValueNormalizer.AsList(operand).Any(candidate => ComparisonHelper.EqualsValue(rowValue, candidate)):
                case "nin" when InputValueNormalizer.AsList(operand).Any(candidate => ComparisonHelper.EqualsValue(rowValue, candidate)):
                    return false;
            }
        }

        return true;
    }

    private IReadOnlyList<object> ApplyRowSort(Type elementType, IReadOnlyList<object> rows, object? order)
    {
        var clauses = ParseSimpleOrderClauses(order);
        if (clauses.Count == 0)
        {
            return rows;
        }

        IOrderedEnumerable<object>? ordered = null;
        foreach (var clause in clauses)
        {
            Func<object, object?> selector = row => ResolveFieldValue(elementType, row, clause.FieldName, isFlat: false);
            ordered = ordered is null
                ? ApplyOrder(rows, selector, clause.Descending)
                : ApplyThenOrder(ordered, selector, clause.Descending);
        }

        return ordered!.ToArray();
    }

    private IReadOnlyList<object> ApplyFlatSort(IReadOnlyList<object> rows, object? order)
    {
        var clauses = ParseSimpleOrderClauses(order);
        if (clauses.Count == 0)
        {
            return rows;
        }

        IOrderedEnumerable<object>? ordered = null;
        foreach (var clause in clauses)
        {
            Func<object, object?> selector = row => ((IReadOnlyDictionary<string, object?>)row).GetValueOrDefault(clause.FieldName);
            ordered = ordered is null
                ? ApplyOrder(rows, selector, clause.Descending)
                : ApplyThenOrder(ordered, selector, clause.Descending);
        }

        return ordered!.ToArray();
    }

    private IReadOnlyList<GroupRowResult> ApplyGroupSort(IReadOnlyList<GroupRowResult> rows, object? order)
    {
        var clauses = ParseGroupOrderClauses(order);
        if (clauses.Count == 0)
        {
            return rows;
        }

        IOrderedEnumerable<GroupRowResult>? ordered = null;
        foreach (var clause in clauses)
        {
            Func<GroupRowResult, object?> selector = clause.Kind switch
            {
                GroupOrderKind.Key => row => row.Key.GetValueOrDefault(clause.FieldName),
                GroupOrderKind.Count => row => row.Selection.Rows.Count,
                _ => row => ResolveAggregateProjectionField(new AggregateProjection(row.Selection, clause.Operator, null, null), clause.FieldName)
            };

            ordered = ordered is null
                ? ApplyOrder(rows, selector, clause.Descending)
                : ApplyThenOrder(ordered, selector, clause.Descending);
        }

        return ordered!.ToArray();
    }

    private static IOrderedEnumerable<T> ApplyOrder<T>(
        IEnumerable<T> rows,
        Func<T, object?> selector,
        bool descending) =>
        descending
            ? rows.OrderByDescending(selector, ProjectionComparer.Instance)
            : rows.OrderBy(selector, ProjectionComparer.Instance);

    private static IOrderedEnumerable<T> ApplyThenOrder<T>(
        IOrderedEnumerable<T> rows,
        Func<T, object?> selector,
        bool descending) =>
        descending
            ? rows.ThenByDescending(selector, ProjectionComparer.Instance)
            : rows.ThenBy(selector, ProjectionComparer.Instance);

    private static IReadOnlyList<object> ApplyWindow(IReadOnlyList<object> rows, int? offset, int? limit)
    {
        IEnumerable<object> query = rows;
        if (offset is > 0)
        {
            query = query.Skip(offset.Value);
        }

        if (limit is >= 0)
        {
            query = query.Take(limit.Value);
        }

        return query.ToArray();
    }

    private static IReadOnlyList<GroupRowResult> ApplyGroupWindow(IReadOnlyList<GroupRowResult> rows, int? offset, int? limit)
    {
        IEnumerable<GroupRowResult> query = rows;
        if (offset is > 0)
        {
            query = query.Skip(offset.Value);
        }

        if (limit is >= 0)
        {
            query = query.Take(limit.Value);
        }

        return query.ToArray();
    }

    private static IReadOnlyList<object> ToObjectRows(object? sourceValue)
    {
        if (sourceValue is null)
        {
            return [];
        }

        return sourceValue switch
        {
            IEnumerable<object> typed => typed.ToArray(),
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().ToArray(),
            _ => [sourceValue]
        };
    }

    private static string FormatOrderCacheKey(IReadOnlyList<(string FieldName, bool Descending)>? order) =>
        order is null || order.Count == 0
            ? string.Empty
            : string.Join("|", order.Select(clause => $"{clause.FieldName}:{clause.Descending}"));

    private IReadOnlyList<object> ExpandRows(
        CollectionFieldModel collectionField,
        IReadOnlyList<object> baseRows,
        IReadOnlyList<string> expand)
    {
        var flatShape = FlatRowShapeCache.GetOrCreate(collectionField);
        var selectedPaths = expand
            .Select(path => flatShape.Paths.FirstOrDefault(candidate => candidate.Path == path))
            .ToArray();

        if (selectedPaths.Any(path => path is null))
        {
            throw new InvalidOperationException("One or more flat expand paths are invalid.");
        }

        var expanded = new List<object>();

        foreach (var baseRow in baseRows)
        {
            var pathRows = selectedPaths
                .Cast<FlatPathModel>()
                .Select(path => ResolveTerminalRows(baseRow, path))
                .ToArray();

            if (pathRows.Any(rows => rows.Count == 0))
            {
                continue;
            }

            foreach (var combination in Cartesian(pathRows))
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);

                foreach (var property in collectionField.ElementType.GetProperties().Where(p => p.CanRead && TypeInspection.IsScalar(p.PropertyType)))
                {
                    row[GraphQlNaming.GetFieldName(property)] = property.GetValue(baseRow);
                }

                foreach (var item in combination)
                {
                    foreach (var property in item.Path.TerminalElementType.GetProperties().Where(p => p.CanRead && TypeInspection.IsScalar(p.PropertyType)))
                    {
                        row[item.Path.Prefix + GraphQlNaming.ToPascalCase(GraphQlNaming.GetFieldName(property))] = property.GetValue(item.Value);
                    }
                }

                expanded.Add(row);
            }
        }

        return expanded;
    }

    private static IReadOnlyList<(FlatPathModel Path, object Value)> ResolveTerminalRows(object baseRow, FlatPathModel path)
    {
        var currentRows = new List<object> { baseRow };

        foreach (var segment in path.Segments)
        {
            var nextRows = new List<object>();

            foreach (var currentRow in currentRows)
            {
                var value = MemberAccessor.GetValue(segment, currentRow);
                if (value is null)
                {
                    continue;
                }

                if (value is System.Collections.IEnumerable enumerable and not string)
                {
                    nextRows.AddRange(enumerable.Cast<object>());
                }
                else
                {
                    nextRows.Add(value);
                }
            }

            currentRows = nextRows;
        }

        return currentRows.Select(value => (path, value)).ToArray();
    }

    private void ValidateFlatReferences(CollectionFieldModel collectionField, IReadOnlyList<string> expand, object? where, object? order)
    {
        var flatShape = FlatRowShapeCache.GetOrCreate(collectionField);
        var allowedGeneratedFields = flatShape.GeneratedFieldOwners
            .Where(kvp => expand.Contains(kvp.Value.Path, StringComparer.Ordinal))
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.Ordinal);

        var invalidFields = CollectReferencedFields(where)
            .Concat(ParseSimpleOrderClauses(order).Select(clause => clause.FieldName))
            .Where(field => flatShape.GeneratedFieldOwners.ContainsKey(field) && !allowedGeneratedFields.Contains(field))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (invalidFields.Length > 0)
        {
            throw new InvalidOperationException(
                $"The requested flat rowset does not include generated fields: {string.Join(", ", invalidFields)}.");
        }
    }

    private static IEnumerable<string> CollectReferencedFields(object? input)
    {
        var dictionary = InputValueNormalizer.AsDictionary(input);

        foreach (var (key, value) in dictionary)
        {
            if (InputValueNormalizer.IsEmpty(value))
            {
                continue;
            }

            if (key is "and" or "or")
            {
                foreach (var child in InputValueNormalizer.AsList(value))
                {
                    foreach (var field in CollectReferencedFields(child))
                    {
                        yield return field;
                    }
                }

                continue;
            }

            if (key == "not")
            {
                foreach (var field in CollectReferencedFields(value))
                {
                    yield return field;
                }

                continue;
            }

            yield return key;
        }
    }

    private bool PassesHaving(AggregateSelectionContext selection, object? having) =>
        having is null || MatchesHaving(selection, InputValueNormalizer.AsDictionary(having));

    private bool MatchesHaving(AggregateSelectionContext selection, IReadOnlyDictionary<string, object?> having)
    {
        foreach (var (key, value) in having)
        {
            if (InputValueNormalizer.IsEmpty(value))
            {
                continue;
            }

            switch (key)
            {
                case "and":
                    if (!InputValueNormalizer.AsList(value).All(item => MatchesHaving(selection, InputValueNormalizer.AsDictionary(item))))
                    {
                        return false;
                    }

                    continue;

                case "or":
                    if (!InputValueNormalizer.AsList(value).Any(item => MatchesHaving(selection, InputValueNormalizer.AsDictionary(item))))
                    {
                        return false;
                    }

                    continue;

                case "not":
                    if (MatchesHaving(selection, InputValueNormalizer.AsDictionary(value)))
                    {
                        return false;
                    }

                    continue;

                case "count":
                    if (!MatchesScalarOperations(selection.Rows.Count, InputValueNormalizer.AsDictionary(value)))
                    {
                        return false;
                    }

                    continue;
            }

            var aggregateOperator = ParseOperator(key);
            foreach (var (fieldName, operations) in InputValueNormalizer.AsDictionary(value))
            {
                if (InputValueNormalizer.IsEmpty(operations))
                {
                    continue;
                }

                var projectedValue = ResolveAggregateProjectionField(
                    new AggregateProjection(selection, aggregateOperator, separator: null, order: null),
                    fieldName);

                if (!MatchesScalarOperations(projectedValue, InputValueNormalizer.AsDictionary(operations)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private int GetDistinctCount(AggregateSelectionContext selection, string fieldName) =>
        TryResolveQueryableAggregate(selection, fieldName, AggregateOperator.CountDistinct, out var value)
            ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)
            : selection.Rows
                .Select(row => selection.IsFlat
                    ? ((IReadOnlyDictionary<string, object?>)row).GetValueOrDefault(fieldName)
                    : ResolveFieldValue(selection.CollectionField.ElementType, row, fieldName, isFlat: false))
                .Where(value => value is not null)
                .Distinct(ProjectionEqualityComparer.Instance)
                .Count();

    private object? GetMinOrMax(AggregateSelectionContext selection, string fieldName, bool min)
    {
        if (TryResolveQueryableAggregate(selection, fieldName, min ? AggregateOperator.Min : AggregateOperator.Max, out var queryableValue))
        {
            return queryableValue;
        }

        var values = selection.Rows
            .Select(row => selection.IsFlat
                ? ((IReadOnlyDictionary<string, object?>)row).GetValueOrDefault(fieldName)
                : ResolveFieldValue(selection.CollectionField.ElementType, row, fieldName, isFlat: false))
            .Where(value => value is not null)
            .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        return min
            ? values.MinBy(value => value, ProjectionComparer.Instance)
            : values.MaxBy(value => value, ProjectionComparer.Instance);
    }

    private object? GetStringAggregate(
        AggregateSelectionContext selection,
        string fieldName,
        string separator,
        IReadOnlyList<(string FieldName, bool Descending)>? order,
        bool distinct)
    {
        IReadOnlyList<object> rows = selection.Rows;

        if (order is not null)
        {
            if (order.Count > 0)
            {
                rows = selection.IsFlat
                    ? ApplyFlatSort(rows, BuildSimpleOrder(order))
                    : ApplyRowSort(selection.CollectionField.ElementType, rows, BuildSimpleOrder(order));
            }
        }

        var values = rows
            .Select(row => selection.IsFlat
                ? ((IReadOnlyDictionary<string, object?>)row).GetValueOrDefault(fieldName)?.ToString()
                : ResolveFieldValue(selection.CollectionField.ElementType, row, fieldName, isFlat: false)?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>();

        if (distinct)
        {
            values = values.Distinct(StringComparer.Ordinal);
        }

        return string.Join(separator, values);
    }

    private static object BuildSimpleOrder(IReadOnlyList<(string FieldName, bool Descending)> clauses) =>
        clauses
            .Select(clause => new Dictionary<string, object?> { [clause.FieldName] = clause.Descending ? "DESC" : "ASC" })
            .ToArray();

    private object? GetNumericAggregate(AggregateSelectionContext selection, string fieldName, NumericAggregate aggregate)
    {
        AggregateOperator? providerAwareOperator = aggregate switch
        {
            NumericAggregate.Sum => AggregateOperator.Sum,
            NumericAggregate.Average => AggregateOperator.Avg,
            NumericAggregate.SampleStdev => AggregateOperator.Stdev,
            NumericAggregate.PopulationStdev => AggregateOperator.Stdevp,
            NumericAggregate.Skew => AggregateOperator.Skew,
            NumericAggregate.Kurtosis => AggregateOperator.Kurtosis,
            _ => null
        };

        if (providerAwareOperator is { } operatorValue &&
            TryResolveQueryableAggregate(selection, fieldName, operatorValue, out var queryableValue))
        {
            return queryableValue;
        }

        var values = TryGetQueryableProjectedNumericValues(selection, fieldName, out var projectedValues)
            ? projectedValues
            : selection.Rows
                .Select(row => selection.IsFlat
                    ? ((IReadOnlyDictionary<string, object?>)row).GetValueOrDefault(fieldName)
                    : ResolveFieldValue(selection.CollectionField.ElementType, row, fieldName, isFlat: false))
                .Where(value => ComparisonHelper.TryConvertToDouble(value, out _))
                .Select(value =>
                {
                    ComparisonHelper.TryConvertToDouble(value, out var number);
                    return number;
                })
                .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        return aggregate switch
        {
            NumericAggregate.Sum => values.Sum(),
            NumericAggregate.Average => values.Average(),
            NumericAggregate.SampleStdev => RunningMoments.Calculate(values).SampleStandardDeviation,
            NumericAggregate.PopulationStdev => RunningMoments.Calculate(values).PopulationStandardDeviation,
            NumericAggregate.Skew => RunningMoments.Calculate(values).Skewness,
            NumericAggregate.Kurtosis => RunningMoments.Calculate(values).Kurtosis,
            _ => null
        };
    }

    private static List<(string FieldName, bool Descending)> ParseSimpleOrderClauses(object? order)
    {
        var clauses = new List<(string FieldName, bool Descending)>();

        foreach (var item in InputValueNormalizer.AsList(order))
        {
            if (InputValueNormalizer.IsEmpty(item))
            {
                continue;
            }

            foreach (var (key, value) in InputValueNormalizer.AsDictionary(item))
            {
                if (InputValueNormalizer.IsEmpty(value))
                {
                    continue;
                }

                clauses.Add((key, string.Equals(value!.ToString(), "DESC", StringComparison.Ordinal)));
            }
        }

        return clauses;
    }

    private static List<GroupOrderClause> ParseGroupOrderClauses(object? order)
    {
        var clauses = new List<GroupOrderClause>();

        foreach (var item in InputValueNormalizer.AsList(order))
        {
            if (InputValueNormalizer.IsEmpty(item))
            {
                continue;
            }

            foreach (var (key, value) in InputValueNormalizer.AsDictionary(item))
            {
                if (InputValueNormalizer.IsEmpty(value))
                {
                    continue;
                }

                switch (key)
                {
                    case "key":
                        foreach (var (fieldName, sortDirection) in InputValueNormalizer.AsDictionary(value))
                        {
                            if (InputValueNormalizer.IsEmpty(sortDirection))
                            {
                                continue;
                            }

                            clauses.Add(new GroupOrderClause(GroupOrderKind.Key, fieldName, string.Equals(sortDirection!.ToString(), "DESC", StringComparison.Ordinal), default));
                        }

                        break;

                    case "count":
                        clauses.Add(new GroupOrderClause(GroupOrderKind.Count, key, string.Equals(value!.ToString(), "DESC", StringComparison.Ordinal), default));
                        break;

                    default:
                        foreach (var (fieldName, sortDirection) in InputValueNormalizer.AsDictionary(value))
                        {
                            if (InputValueNormalizer.IsEmpty(sortDirection))
                            {
                                continue;
                            }

                            clauses.Add(new GroupOrderClause(GroupOrderKind.Operator, fieldName, string.Equals(sortDirection!.ToString(), "DESC", StringComparison.Ordinal), ParseOperator(key)));
                        }

                        break;
                }
            }
        }

        return clauses;
    }

    private IReadOnlyDictionary<string, object?> BuildGroupKey(
        CollectionFieldModel collectionField,
        object row,
        IReadOnlyList<string> fields,
        bool isFlat)
    {
        var key = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            key[field] = isFlat
                ? ((IReadOnlyDictionary<string, object?>)row).GetValueOrDefault(field)
                : ResolveFieldValue(collectionField.ElementType, row, field, isFlat: false);
        }

        return key;
    }

    private static int? ConvertToNullableInt(object? value) =>
        value switch
        {
            null => null,
            int intValue => intValue,
            long longValue => (int)longValue,
            _ => int.Parse(value.ToString()!, System.Globalization.CultureInfo.InvariantCulture)
        };

    private static AggregateOperator ParseOperator(string name) =>
        name switch
        {
            "countDistinct" => AggregateOperator.CountDistinct,
            "sum" => AggregateOperator.Sum,
            "avg" => AggregateOperator.Avg,
            "min" => AggregateOperator.Min,
            "max" => AggregateOperator.Max,
            "stdev" => AggregateOperator.Stdev,
            "stdevp" => AggregateOperator.Stdevp,
            "skew" => AggregateOperator.Skew,
            "kurtosis" => AggregateOperator.Kurtosis,
            "stringAgg" => AggregateOperator.StringAgg,
            "stringAggDistinct" => AggregateOperator.StringAggDistinct,
            _ => throw new InvalidOperationException($"Unsupported aggregate operator '{name}'.")
        };

    private enum NumericAggregate
    {
        Sum,
        Average,
        SampleStdev,
        PopulationStdev,
        Skew,
        Kurtosis
    }

    private enum GroupOrderKind
    {
        Key,
        Count,
        Operator
    }

    private readonly record struct GroupOrderClause(
        GroupOrderKind Kind,
        string FieldName,
        bool Descending,
        AggregateOperator Operator);

    private sealed class ProjectionComparer : IComparer<object?>
    {
        public static ProjectionComparer Instance { get; } = new();

        public int Compare(object? x, object? y) => ComparisonHelper.Compare(x, y);
    }

    private sealed class ProjectionEqualityComparer : IEqualityComparer<object?>
    {
        public static ProjectionEqualityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ComparisonHelper.EqualsValue(x, y);

        public int GetHashCode(object? obj) => obj?.GetHashCode() ?? 0;
    }

    private sealed class GroupKeyComparer : IEqualityComparer<IReadOnlyDictionary<string, object?>>
    {
        public static GroupKeyComparer Instance { get; } = new();

        public bool Equals(IReadOnlyDictionary<string, object?>? x, IReadOnlyDictionary<string, object?>? y)
        {
            if (x is null || y is null || x.Count != y.Count)
            {
                return false;
            }

            return x.All(pair => y.TryGetValue(pair.Key, out var value) && ComparisonHelper.EqualsValue(pair.Value, value));
        }

        public int GetHashCode(IReadOnlyDictionary<string, object?> obj)
        {
            var hash = new HashCode();

            foreach (var pair in obj.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                hash.Add(pair.Key, StringComparer.Ordinal);
                hash.Add(pair.Value);
            }

            return hash.ToHashCode();
        }
    }

    private readonly record struct RunningMoments(
        double SampleStandardDeviation,
        double PopulationStandardDeviation,
        double Skewness,
        double Kurtosis)
    {
        public static RunningMoments Calculate(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return default;
            }

            double mean = 0d;
            double m2 = 0d;
            double m3 = 0d;
            double m4 = 0d;
            var count = 0;

            foreach (var value in values)
            {
                var previousCount = count;
                count++;

                var delta = value - mean;
                var deltaN = delta / count;
                var deltaN2 = deltaN * deltaN;
                var term1 = delta * deltaN * previousCount;

                mean += deltaN;
                m4 += term1 * deltaN2 * ((count * count) - (3d * count) + 3d)
                    + (6d * deltaN2 * m2)
                    - (4d * deltaN * m3);
                m3 += (term1 * deltaN * (count - 2d)) - (3d * deltaN * m2);
                m2 += term1;
            }

            var variancePopulation = Math.Max(0d, m2 / count);
            var sampleVariance = count > 1
                ? Math.Max(0d, m2 / (count - 1d))
                : 0d;
            var populationStdev = Math.Sqrt(variancePopulation);
            var sampleStdev = Math.Sqrt(sampleVariance);
            var m2Population = variancePopulation;
            var m3Population = m3 / count;
            var m4Population = m4 / count;
            var skew = m2Population <= 0d ? 0d : m3Population / Math.Pow(m2Population, 1.5d);
            var kurtosis = m2Population <= 0d ? 0d : m4Population / (m2Population * m2Population);
            return new RunningMoments(sampleStdev, populationStdev, skew, kurtosis);
        }
    }

    private static IEnumerable<(FlatPathModel Path, object Value)[]> Cartesian(IReadOnlyList<IReadOnlyList<(FlatPathModel Path, object Value)>> values)
    {
        IEnumerable<(FlatPathModel Path, object Value)[]> seed = [[]];

        foreach (var set in values)
        {
            seed = seed.SelectMany(prefix => set, (prefix, item) => prefix.Append(item).ToArray());
        }

        return seed;
    }
}
