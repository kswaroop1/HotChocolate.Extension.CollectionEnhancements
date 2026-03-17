using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Data;
using HotChocolate.Extension.CollectionEnhancements.Metadata;
using Microsoft.EntityFrameworkCore;

namespace HotChocolate.Extension.CollectionEnhancements.Execution;

internal sealed partial class CollectionExecutionEngine
{
    public object ApplyCollectionArgumentsForField(
        CollectionFieldModel collectionField,
        object? sourceValue,
        object? where,
        object? order,
        int? offset,
        int? limit)
    {
        if (TryApplyQueryableCollectionArguments(collectionField, sourceValue, where, order, offset, limit, out var queryableResult))
        {
            return WrapAsExecutable(collectionField.ElementType, queryableResult!);
        }

        return WrapAsExecutable(
            collectionField.ElementType,
            ApplyCollectionArguments(collectionField, sourceValue, where, order, offset, limit));
    }

    internal bool TryApplyQueryableCollectionArguments(
        CollectionFieldModel collectionField,
        object? sourceValue,
        object? where,
        object? order,
        int? offset,
        int? limit,
        out object? result)
    {
        result = null;

        if (sourceValue is not IQueryable queryable)
        {
            return false;
        }

        if (!TryApplyQueryableObjectFilter(collectionField.ElementType, queryable, where, out var filtered))
        {
            return false;
        }

        if (!TryApplyQueryableSort(collectionField.ElementType, filtered, order, out var ordered))
        {
            return false;
        }

        result = ApplyQueryableWindow(collectionField.ElementType, ordered, offset, limit);
        return true;
    }

    private bool TryApplyQueryableObjectFilter(
        Type elementType,
        IQueryable queryable,
        object? where,
        out IQueryable result)
    {
        result = queryable;

        if (where is null)
        {
            return true;
        }

        var normalizedWhere = InputValueNormalizer.AsDictionary(where);
        if (normalizedWhere.Count == 0)
        {
            return true;
        }

        var parameter = Expression.Parameter(elementType, "row");
        if (!TryBuildObjectFilterExpression(elementType, parameter, normalizedWhere, out var predicate))
        {
            return false;
        }

        if (predicate is null)
        {
            return true;
        }

        var lambda = Expression.Lambda(predicate, parameter);
        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Where),
            [elementType],
            queryable.Expression,
            Expression.Quote(lambda));

        result = queryable.Provider.CreateQuery(call);
        return true;
    }

    private bool TryBuildObjectFilterExpression(
        Type elementType,
        Expression instance,
        IReadOnlyDictionary<string, object?> filter,
        out Expression? expression)
    {
        var model = _catalog.TryGetObjectType(elementType);
        if (model is null)
        {
            expression = null;
            return false;
        }

        var parts = new List<Expression>();

        foreach (var (name, value) in filter)
        {
            if (InputValueNormalizer.IsEmpty(value))
            {
                continue;
            }

            switch (name)
            {
                case "and":
                    if (!TryBuildLogicalListExpression(elementType, instance, value, isOr: false, out var andExpression))
                    {
                        expression = null;
                        return false;
                    }

                    if (andExpression is not null)
                    {
                        parts.Add(andExpression);
                    }

                    continue;

                case "or":
                    if (!TryBuildLogicalListExpression(elementType, instance, value, isOr: true, out var orExpression))
                    {
                        expression = null;
                        return false;
                    }

                    if (orExpression is not null)
                    {
                        parts.Add(orExpression);
                    }

                    continue;

                case "not":
                    if (!TryBuildObjectFilterExpression(elementType, instance, InputValueNormalizer.AsDictionary(value), out var notExpression))
                    {
                        expression = null;
                        return false;
                    }

                    parts.Add(notExpression is null
                        ? Expression.Constant(false)
                        : Expression.Not(notExpression));
                    continue;
            }

            if (model.FindScalar(name) is { } scalar)
            {
                if (!TryBuildScalarOperationsExpression(BuildMemberAccess(instance, scalar.Member), InputValueNormalizer.AsDictionary(value), out var scalarExpression))
                {
                    expression = null;
                    return false;
                }

                if (scalarExpression is not null)
                {
                    parts.Add(scalarExpression);
                }

                continue;
            }

            if (model.FindObject(name) is { } objectField)
            {
                var nestedInstance = BuildMemberAccess(instance, objectField.Member);
                if (!TryBuildObjectFilterExpression(objectField.ClrType, nestedInstance, InputValueNormalizer.AsDictionary(value), out var nestedExpression))
                {
                    expression = null;
                    return false;
                }

                if (nestedExpression is not null)
                {
                    parts.Add(GuardNull(nestedInstance, nestedExpression));
                }

                continue;
            }

            if (model.CollectionFields.FirstOrDefault(field => field.AggregateFieldName == name) is { } aggregateField)
            {
                if (!TryBuildAggregateCriteriaExpression(instance, aggregateField, value, out var aggregateExpression))
                {
                    expression = null;
                    return false;
                }

                if (aggregateExpression is not null)
                {
                    parts.Add(aggregateExpression);
                }

                continue;
            }

            if (model.CollectionFields.FirstOrDefault(field => field.GroupFieldName == name) is { } groupField)
            {
                if (!TryBuildGroupCriteriaExpression(instance, groupField, value, out var groupExpression))
                {
                    expression = null;
                    return false;
                }

                if (groupExpression is not null)
                {
                    parts.Add(groupExpression);
                }

                continue;
            }

            expression = null;
            return false;
        }

        expression = CombineAnd(parts);
        return true;
    }

    private bool TryBuildLogicalListExpression(
        Type elementType,
        Expression instance,
        object? value,
        bool isOr,
        out Expression? expression)
    {
        var parts = new List<Expression>();
        var hasItems = false;

        foreach (var item in InputValueNormalizer.AsList(value))
        {
            hasItems = true;

            if (!TryBuildObjectFilterExpression(elementType, instance, InputValueNormalizer.AsDictionary(item), out var child))
            {
                expression = null;
                return false;
            }

            parts.Add(child!);
        }

        if (isOr)
        {
            if (!hasItems)
            {
                expression = Expression.Constant(false);
                return true;
            }

            expression = CombineOr(parts);
            return true;
        }

        expression = CombineAnd(parts);
        return true;
    }

    private bool TryBuildAggregateCriteriaExpression(
        Expression ownerInstance,
        CollectionFieldModel collectionField,
        object? criteriaValue,
        out Expression? expression)
    {
        var criteria = InputValueNormalizer.AsDictionary(criteriaValue);
        if (criteria.Count == 0)
        {
            expression = null;
            return true;
        }

        if (!criteria.TryGetValue("having", out var havingValue) || InputValueNormalizer.IsEmpty(havingValue))
        {
            expression = null;
            return true;
        }

        var collection = EnsureEnumerableCollection(BuildMemberAccess(ownerInstance, collectionField.Member), collectionField.ElementType);

        if (criteria.TryGetValue("where", out var whereValue) &&
            !TryApplyEnumerableObjectFilter(collectionField.ElementType, collection, whereValue, out collection))
        {
            expression = null;
            return false;
        }

        return TryBuildHavingExpressionForSequence(
            collectionField,
            collection,
            InputValueNormalizer.AsDictionary(havingValue),
            out expression);
    }

    private bool TryBuildGroupCriteriaExpression(
        Expression ownerInstance,
        CollectionFieldModel collectionField,
        object? criteriaValue,
        out Expression? expression)
    {
        var criteria = InputValueNormalizer.AsDictionary(criteriaValue);
        if (criteria.Count == 0)
        {
            expression = null;
            return true;
        }

        var groupByFields = InputValueNormalizer.AsList(criteria.TryGetValue("by", out var byValue) ? byValue : null)
            .Select(value => value?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        if (groupByFields.Length == 0)
        {
            expression = Expression.Constant(false);
            return true;
        }

        if ((criteria.TryGetValue("order", out var orderValue) && !InputValueNormalizer.IsEmpty(orderValue)) ||
            (criteria.TryGetValue("offset", out var offsetValue) && ConvertToNullableInt(offsetValue) is not null) ||
            (criteria.TryGetValue("limit", out var limitValue) && ConvertToNullableInt(limitValue) is not null))
        {
            expression = null;
            return false;
        }

        if (_catalog.TryGetObjectType(collectionField.ElementType) is not { } model)
        {
            expression = null;
            return false;
        }

        var keyFields = new List<ScalarFieldModel>(groupByFields.Length);
        foreach (var fieldName in groupByFields)
        {
            if (model.FindScalar(fieldName) is not { } scalarField)
            {
                expression = null;
                return false;
            }

            keyFields.Add(scalarField);
        }

        var collection = EnsureEnumerableCollection(BuildMemberAccess(ownerInstance, collectionField.Member), collectionField.ElementType);

        if (criteria.TryGetValue("where", out var whereValue) &&
            !TryApplyEnumerableObjectFilter(collectionField.ElementType, collection, whereValue, out collection))
        {
            expression = null;
            return false;
        }

        var groupParameter = Expression.Parameter(collectionField.ElementType, "item");
        var keySelector = BuildKeySelectorLambda(collectionField.ElementType, keyFields, groupParameter, out var keyType);
        var grouped = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.GroupBy),
            [collectionField.ElementType, keyType],
            collection,
            keySelector);

        var groupingType = typeof(IGrouping<,>).MakeGenericType(keyType, collectionField.ElementType);
        if (!criteria.TryGetValue("having", out var havingValue) || InputValueNormalizer.IsEmpty(havingValue))
        {
            expression = Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Any),
                [groupingType],
                grouped);
            return true;
        }

        var groupingParameter = Expression.Parameter(groupingType, "group");
        if (!TryBuildHavingExpressionForSequence(
                collectionField,
                groupingParameter,
                InputValueNormalizer.AsDictionary(havingValue),
                out var groupPredicate))
        {
            expression = null;
            return false;
        }

        expression = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Any),
            [groupingType],
            grouped,
            Expression.Lambda(groupPredicate!, groupingParameter));
        return true;
    }

    private bool TryApplyEnumerableObjectFilter(
        Type elementType,
        Expression source,
        object? where,
        out Expression filtered)
    {
        filtered = source;

        if (where is null)
        {
            return true;
        }

        var normalizedWhere = InputValueNormalizer.AsDictionary(where);
        if (normalizedWhere.Count == 0)
        {
            return true;
        }

        var parameter = Expression.Parameter(elementType, "item");
        if (!TryBuildObjectFilterExpression(elementType, parameter, normalizedWhere, out var predicate))
        {
            return false;
        }

        if (predicate is null)
        {
            return true;
        }

        filtered = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Where),
            [elementType],
            source,
            Expression.Lambda(predicate, parameter));
        return true;
    }

    private bool TryBuildHavingExpressionForSequence(
        CollectionFieldModel collectionField,
        Expression sequence,
        IReadOnlyDictionary<string, object?> having,
        out Expression? expression)
    {
        var parts = new List<Expression>();

        foreach (var (key, value) in having)
        {
            if (InputValueNormalizer.IsEmpty(value))
            {
                continue;
            }

            switch (key)
            {
                case "and":
                    if (!TryBuildLogicalHavingExpression(collectionField, sequence, value, isOr: false, out var andExpression))
                    {
                        expression = null;
                        return false;
                    }

                    if (andExpression is not null)
                    {
                        parts.Add(andExpression);
                    }

                    continue;

                case "or":
                    if (!TryBuildLogicalHavingExpression(collectionField, sequence, value, isOr: true, out var orExpression))
                    {
                        expression = null;
                        return false;
                    }

                    if (orExpression is not null)
                    {
                        parts.Add(orExpression);
                    }

                    continue;

                case "not":
                    if (!TryBuildHavingExpressionForSequence(
                            collectionField,
                            sequence,
                            InputValueNormalizer.AsDictionary(value),
                            out var notExpression))
                    {
                        expression = null;
                        return false;
                    }

                    parts.Add(Expression.Not(notExpression!));
                    continue;

                case "count":
                {
                    var count = Expression.Call(
                        typeof(Enumerable),
                        nameof(Enumerable.Count),
                        [collectionField.ElementType],
                        sequence);

                    if (!TryBuildScalarOperationsExpression(count, InputValueNormalizer.AsDictionary(value), out var countExpression))
                    {
                        expression = null;
                        return false;
                    }

                    if (countExpression is not null)
                    {
                        parts.Add(countExpression);
                    }

                    continue;
                }
            }

            if (!TryParseSupportedQueryableAggregateOperator(key, out var aggregateOperator))
            {
                expression = null;
                return false;
            }

            foreach (var (fieldName, operations) in InputValueNormalizer.AsDictionary(value))
            {
                if (InputValueNormalizer.IsEmpty(operations))
                {
                    continue;
                }

                if (!TryBuildQueryableAggregateValueExpression(collectionField, sequence, fieldName, aggregateOperator, out var aggregateValue))
                {
                    expression = null;
                    return false;
                }

                if (!TryBuildScalarOperationsExpression(aggregateValue, InputValueNormalizer.AsDictionary(operations), out var aggregateExpression))
                {
                    expression = null;
                    return false;
                }

                if (aggregateExpression is not null)
                {
                    parts.Add(aggregateExpression);
                }
            }
        }

        expression = CombineAnd(parts) ?? Expression.Constant(true);
        return true;
    }

    private bool TryBuildLogicalHavingExpression(
        CollectionFieldModel collectionField,
        Expression sequence,
        object? value,
        bool isOr,
        out Expression? expression)
    {
        var parts = new List<Expression>();
        var hasItems = false;

        foreach (var item in InputValueNormalizer.AsList(value))
        {
            hasItems = true;

            if (!TryBuildHavingExpressionForSequence(collectionField, sequence, InputValueNormalizer.AsDictionary(item), out var child))
            {
                expression = null;
                return false;
            }

            parts.Add(child!);
        }

        if (isOr)
        {
            expression = !hasItems
                ? Expression.Constant(false)
                : CombineOr(parts);
            return true;
        }

        expression = CombineAnd(parts);
        return true;
    }

    private static Expression? CombineAnd(IReadOnlyList<Expression> expressions) =>
        expressions.Aggregate(default(Expression), static (current, next) =>
            current is null ? next : Expression.AndAlso(current, next));

    private static Expression? CombineOr(IReadOnlyList<Expression> expressions) =>
        expressions.Aggregate(default(Expression), static (current, next) =>
            current is null ? next : Expression.OrElse(current, next));

    private static Expression GuardNull(Expression instance, Expression guardedExpression)
    {
        if (!CanBeNull(instance.Type))
        {
            return guardedExpression;
        }

        return Expression.AndAlso(
            Expression.NotEqual(instance, Expression.Constant(null, instance.Type)),
            guardedExpression);
    }

    private static bool TryBuildScalarOperationsExpression(
        Expression member,
        IReadOnlyDictionary<string, object?> operations,
        out Expression? expression)
    {
        var parts = new List<Expression>();

        foreach (var (operation, operand) in operations)
        {
            if (InputValueNormalizer.IsEmpty(operand))
            {
                continue;
            }

            if (!TryBuildScalarOperationExpression(member, operation, operand, out var operationExpression))
            {
                expression = null;
                return false;
            }

            if (operationExpression is not null)
            {
                parts.Add(operationExpression);
            }
        }

        expression = CombineAnd(parts);
        return true;
    }

    private static bool TryBuildScalarOperationExpression(
        Expression member,
        string operation,
        object? operand,
        out Expression? expression)
    {
        expression = null;

        switch (operation)
        {
            case "eq":
            case "neq":
                if (!TryCreateTypedConstant(member.Type, operand, out var equalityConstant))
                {
                    return false;
                }

                expression = operation == "eq"
                    ? Expression.Equal(member, equalityConstant)
                    : Expression.NotEqual(member, equalityConstant);
                return true;

            case "in":
            case "nin":
                if (!TryBuildSetExpression(member, InputValueNormalizer.AsList(operand), out var setExpression))
                {
                    return false;
                }

                expression = operation == "in"
                    ? setExpression
                    : Expression.Not(setExpression);
                return true;

            case "gt":
            case "gte":
            case "lt":
            case "lte":
                return TryBuildOrderedComparisonExpression(member, operation, operand, out expression);

            default:
                return true;
        }
    }

    private static bool TryBuildSetExpression(
        Expression member,
        IReadOnlyList<object?> operands,
        out Expression expression)
    {
        var values = Array.CreateInstance(member.Type, operands.Count);

        for (var index = 0; index < operands.Count; index++)
        {
            if (!TryConvertValue(operands[index], member.Type, out var convertedValue))
            {
                expression = null!;
                return false;
            }

            values.SetValue(convertedValue, index);
        }

        expression = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [member.Type],
            Expression.Constant(values, values.GetType()),
            member);

        return true;
    }

    private static bool TryBuildOrderedComparisonExpression(
        Expression member,
        string operation,
        object? operand,
        out Expression? expression)
    {
        expression = null;

        if (!CanUseOrderedComparison(member.Type))
        {
            return false;
        }

        if (!TryCreateTypedConstant(member.Type, operand, out var constant))
        {
            return false;
        }

        if (member.Type == typeof(string))
        {
            var compare = Expression.Call(typeof(string), nameof(string.Compare), Type.EmptyTypes, member, constant);
            var comparison = BuildNumericComparison(compare, operation);
            expression = BuildNullableComparison(member, comparison, operation);
            return true;
        }

        var directComparison = operation switch
        {
            "gt" => Expression.GreaterThan(member, constant),
            "gte" => Expression.GreaterThanOrEqual(member, constant),
            "lt" => Expression.LessThan(member, constant),
            "lte" => Expression.LessThanOrEqual(member, constant),
            _ => null
        };

        if (directComparison is null)
        {
            return false;
        }

        expression = BuildNullableComparison(member, directComparison, operation);
        return true;
    }

    private static Expression BuildNullableComparison(Expression member, Expression comparison, string operation)
    {
        if (!CanBeNull(member.Type))
        {
            return comparison;
        }

        var isNull = Expression.Equal(member, Expression.Constant(null, member.Type));

        return operation switch
        {
            "lt" or "lte" => Expression.OrElse(isNull, comparison),
            _ => Expression.AndAlso(Expression.Not(isNull), comparison)
        };
    }

    private static Expression BuildNumericComparison(Expression compareExpression, string operation)
    {
        var zero = Expression.Constant(0);

        return operation switch
        {
            "gt" => Expression.GreaterThan(compareExpression, zero),
            "gte" => Expression.GreaterThanOrEqual(compareExpression, zero),
            "lt" => Expression.LessThan(compareExpression, zero),
            "lte" => Expression.LessThanOrEqual(compareExpression, zero),
            _ => throw new InvalidOperationException($"Unsupported comparison operation '{operation}'.")
        };
    }

    private static bool TryCreateTypedConstant(Type targetType, object? value, out Expression constant)
    {
        if (!TryConvertValue(value, targetType, out var converted))
        {
            constant = null!;
            return false;
        }

        if (converted is null)
        {
            constant = Expression.Constant(null, targetType);
            return true;
        }

        var actualType = converted.GetType();
        var rawConstant = Expression.Constant(converted, actualType);
        constant = actualType == targetType
            ? rawConstant
            : Expression.Convert(rawConstant, targetType);

        return true;
    }

    private static bool TryConvertValue(object? value, Type targetType, out object? converted)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        var actualType = nullableType ?? targetType;

        if (value is null)
        {
            converted = null;
            return nullableType is not null || !targetType.IsValueType;
        }

        try
        {
            if (actualType.IsEnum)
            {
                converted = value switch
                {
                    string text => Enum.Parse(actualType, text, ignoreCase: false),
                    Enum enumValue => Enum.Parse(actualType, enumValue.ToString()),
                    _ => Enum.ToObject(actualType, Convert.ChangeType(value, Enum.GetUnderlyingType(actualType), CultureInfo.InvariantCulture)!)
                };

                return true;
            }

            converted = actualType switch
            {
                var type when type == typeof(Guid) => value is Guid guid ? guid : Guid.Parse(value.ToString()!),
                var type when type == typeof(DateOnly) => value switch
                {
                    DateOnly dateOnly => dateOnly,
                    DateTime dateTime => DateOnly.FromDateTime(dateTime),
                    _ => DateOnly.Parse(value.ToString()!, CultureInfo.InvariantCulture)
                },
                var type when type == typeof(DateTime) => value switch
                {
                    DateTime dateTime => dateTime,
                    DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
                    _ => DateTime.Parse(value.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                },
                var type when type == typeof(string) => value.ToString(),
                _ => Convert.ChangeType(value, actualType, CultureInfo.InvariantCulture)
            };

            return true;
        }
        catch
        {
            converted = null;
            return false;
        }
    }

    private bool TryApplyQueryableSort(Type elementType, IQueryable queryable, object? order, out IQueryable result)
    {
        result = queryable;
        var clauses = ParseSimpleOrderClauses(order);
        if (clauses.Count == 0)
        {
            return true;
        }

        if (_catalog.TryGetObjectType(elementType) is not { } model)
        {
            return false;
        }

        IQueryable current = queryable;
        var firstClause = true;

        foreach (var clause in clauses)
        {
            var scalarField = model.FindScalar(clause.FieldName);
            if (scalarField is null)
            {
                return false;
            }

            var parameter = Expression.Parameter(elementType, "row");
            var member = BuildMemberAccess(parameter, scalarField.Member);
            var keySelector = Expression.Lambda(member, parameter);
            var methodName = firstClause
                ? clause.Descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy)
                : clause.Descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy);

            var call = Expression.Call(
                typeof(Queryable),
                methodName,
                [elementType, member.Type],
                current.Expression,
                Expression.Quote(keySelector));

            current = current.Provider.CreateQuery(call);
            firstClause = false;
        }

        result = current;
        return true;
    }

    private static IQueryable ApplyQueryableWindow(Type elementType, IQueryable queryable, int? offset, int? limit)
    {
        var current = queryable;

        if (offset is > 0)
        {
            var skip = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Skip),
                [elementType],
                current.Expression,
                Expression.Constant(offset.Value));

            current = current.Provider.CreateQuery(skip);
        }

        if (limit is >= 0)
        {
            var take = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Take),
                [elementType],
                current.Expression,
                Expression.Constant(limit.Value));

            current = current.Provider.CreateQuery(take);
        }

        return current;
    }

    private static object WrapAsExecutable(Type elementType, object source)
    {
        var wrapMethod = source is IQueryable
            ? _wrapQueryableExecutableMethod
            : _wrapEnumerableExecutableMethod;

        return wrapMethod.MakeGenericMethod(elementType)
            .Invoke(null, [ConvertToTypedSequence(elementType, source)])!;
    }

    private static object ConvertToTypedSequence(Type elementType, object source)
    {
        if (source is IQueryable queryable && queryable.ElementType == elementType)
        {
            return queryable;
        }

        if (source.GetType().IsArray && source.GetType().GetElementType() == elementType)
        {
            return source;
        }

        var values = source is System.Collections.IEnumerable enumerable
            ? enumerable.Cast<object?>().ToArray()
            : [source];

        var typedArray = Array.CreateInstance(elementType, values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            typedArray.SetValue(values[index], index);
        }

        return typedArray;
    }

    private bool TryCreateQueryableAggregateSelection(
        CollectionFieldModel collectionField,
        object? sourceValue,
        object? where,
        out AggregateSelectionContext selection)
    {
        selection = null!;

        if (sourceValue is not IQueryable queryable)
        {
            return false;
        }

        if (!TryApplyQueryableObjectFilter(collectionField.ElementType, queryable, where, out var filtered))
        {
            return false;
        }

        selection = new AggregateSelectionContext(collectionField, isFlat: false, filtered);
        return true;
    }

    private bool TryCreateQueryableGroupRows(
        CollectionFieldModel collectionField,
        object? sourceValue,
        IReadOnlyList<string> groupByFields,
        object? where,
        out IReadOnlyList<GroupRowResult> groups)
    {
        groups = [];

        if (sourceValue is not IQueryable queryable)
        {
            return false;
        }

        if (_catalog.TryGetObjectType(collectionField.ElementType) is not { } model)
        {
            return false;
        }

        var keyFields = new List<ScalarFieldModel>(groupByFields.Count);
        foreach (var fieldName in groupByFields)
        {
            if (model.FindScalar(fieldName) is not { } scalarField)
            {
                return false;
            }

            keyFields.Add(scalarField);
        }

        if (!TryApplyQueryableObjectFilter(collectionField.ElementType, queryable, where, out var filtered))
        {
            return false;
        }

        if (!TryExecuteQueryableDistinctKeys(filtered, keyFields, out var distinctKeys))
        {
            return false;
        }

        groups = distinctKeys
            .Select(key =>
            {
                var keyDictionary = CreateKeyDictionary(keyFields, key);
                var subgroup = ApplyQueryableKeyFilter(filtered, keyFields, key);
                return new GroupRowResult(keyDictionary, new AggregateSelectionContext(collectionField, isFlat: false, subgroup));
            })
            .ToArray();

        return true;
    }

    private bool TryResolveQueryableAggregate(
        AggregateSelectionContext selection,
        string fieldName,
        AggregateOperator aggregateOperator,
        out object? value)
    {
        value = null;

        if (selection.IsFlat || selection.QueryableSource is not { } queryable)
        {
            return false;
        }

        if (_catalog.TryGetObjectType(selection.CollectionField.ElementType) is not { } model ||
            model.FindScalar(fieldName) is not { } scalarField)
        {
            return false;
        }

        value = aggregateOperator switch
        {
            AggregateOperator.CountDistinct => ExecuteQueryableCountDistinct(queryable, scalarField),
            AggregateOperator.Sum => ExecuteQueryableNumericAggregate(queryable, scalarField, nameof(Queryable.Sum)),
            AggregateOperator.Avg => ExecuteQueryableNumericAggregate(queryable, scalarField, nameof(Queryable.Average)),
            AggregateOperator.Var => TryExecuteQueryableMomentAggregate(queryable, scalarField, aggregateOperator, out var sampleVariance)
                ? sampleVariance
                : UnsupportedAggregateValue.Instance,
            AggregateOperator.Varp => TryExecuteQueryableMomentAggregate(queryable, scalarField, aggregateOperator, out var populationVariance)
                ? populationVariance
                : UnsupportedAggregateValue.Instance,
            AggregateOperator.Min => ExecuteQueryableMinOrMax(queryable, scalarField, min: true),
            AggregateOperator.Max => ExecuteQueryableMinOrMax(queryable, scalarField, min: false),
            AggregateOperator.Stdev => TryExecuteQueryableMomentAggregate(queryable, scalarField, aggregateOperator, out var sampleStdev)
                ? sampleStdev
                : UnsupportedAggregateValue.Instance,
            AggregateOperator.Stdevp => TryExecuteQueryableMomentAggregate(queryable, scalarField, aggregateOperator, out var populationStdev)
                ? populationStdev
                : UnsupportedAggregateValue.Instance,
            AggregateOperator.Skew => TryExecuteQueryableMomentAggregate(queryable, scalarField, aggregateOperator, out var skew)
                ? skew
                : UnsupportedAggregateValue.Instance,
            AggregateOperator.Kurtosis => TryExecuteQueryableMomentAggregate(queryable, scalarField, aggregateOperator, out var kurtosis)
                ? kurtosis
                : UnsupportedAggregateValue.Instance,
            _ => UnsupportedAggregateValue.Instance
        };

        if (ReferenceEquals(value, UnsupportedAggregateValue.Instance))
        {
            value = null;
            return false;
        }

        return true;
    }

    private static int ExecuteQueryableCount(IQueryable queryable)
    {
        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Count),
            [queryable.ElementType],
            queryable.Expression);

        return Convert.ToInt32(queryable.Provider.Execute(call), CultureInfo.InvariantCulture);
    }

    private object ExecuteQueryableCountDistinct(IQueryable queryable, ScalarFieldModel scalarField)
    {
        var parameter = Expression.Parameter(queryable.ElementType, "row");
        var member = BuildMemberAccess(parameter, scalarField.Member);

        Expression source = queryable.Expression;
        if (CanBeNull(member.Type))
        {
            var notNull = Expression.Lambda(
                Expression.NotEqual(member, Expression.Constant(null, member.Type)),
                parameter);

            source = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Where),
                [queryable.ElementType],
                source,
                Expression.Quote(notNull));
        }

        var selected = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            [queryable.ElementType, member.Type],
            source,
            Expression.Quote(Expression.Lambda(member, parameter)));

        var distinct = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Distinct),
            [member.Type],
            selected);

        var count = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Count),
            [member.Type],
            distinct);

        return Convert.ToInt32(queryable.Provider.Execute(count), CultureInfo.InvariantCulture);
    }

    private object? ExecuteQueryableNumericAggregate(IQueryable queryable, ScalarFieldModel scalarField, string methodName)
    {
        if (!TypeInspection.IsNumeric(scalarField.ClrType))
        {
            return UnsupportedAggregateValue.Instance;
        }

        var parameter = Expression.Parameter(queryable.ElementType, "row");
        var member = BuildMemberAccess(parameter, scalarField.Member);
        var convertedMember = Expression.Convert(member, typeof(double?));

        var selected = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            [queryable.ElementType, typeof(double?)],
            queryable.Expression,
            Expression.Quote(Expression.Lambda(convertedMember, parameter)));

        var aggregateCall = Expression.Call(typeof(Queryable), methodName, Type.EmptyTypes, selected);
        var result = queryable.Provider.Execute(aggregateCall);

        return result is null
            ? null
            : Convert.ToDouble(result, CultureInfo.InvariantCulture);
    }

    private bool TryExecuteQueryableMomentAggregate(
        IQueryable queryable,
        ScalarFieldModel scalarField,
        AggregateOperator aggregateOperator,
        out object? value)
    {
        value = null;

        if (_options.StatisticalMomentsExecutionMode == StatisticalMomentsExecutionMode.Stable ||
            !TypeInspection.IsNumeric(scalarField.ClrType) ||
            !EfCoreProviderSupport.TryDetect(queryable, out var providerInfo) ||
            !providerInfo.IsRelational)
        {
            return false;
        }

        if ((aggregateOperator is AggregateOperator.Var or AggregateOperator.Varp) &&
            TryExecuteNativeVariance(queryable, scalarField, providerInfo, aggregateOperator == AggregateOperator.Varp, out value))
        {
            return true;
        }

        if ((aggregateOperator is AggregateOperator.Stdev or AggregateOperator.Stdevp) &&
            TryExecuteNativeStandardDeviation(queryable, scalarField, providerInfo, aggregateOperator == AggregateOperator.Stdevp, out value))
        {
            return true;
        }

        if (!TryCalculateQueryableMomentStatistics(queryable, scalarField, out var statistics))
        {
            return false;
        }

        value = aggregateOperator switch
        {
            AggregateOperator.Var => statistics.GetSampleVariance(),
            AggregateOperator.Varp => statistics.GetPopulationVariance(),
            AggregateOperator.Stdev => statistics.GetSampleStandardDeviation(),
            AggregateOperator.Stdevp => statistics.GetPopulationStandardDeviation(),
            AggregateOperator.Skew => statistics.GetSkewness(),
            AggregateOperator.Kurtosis => statistics.GetKurtosis(),
            _ => null
        };
        return true;
    }

    private bool TryExecuteNativeVariance(
        IQueryable queryable,
        ScalarFieldModel scalarField,
        EfCoreProviderInfo providerInfo,
        bool population,
        out object? value)
    {
        value = null;

        if (!TryGetNativeVarianceMethod(providerInfo.Family, scalarField.ClrType, population, out var method, out var projectionType))
        {
            return false;
        }

        if (!TryCreateQueryableProjection(queryable, scalarField, projectionType, out var projected))
        {
            return false;
        }

        var count = ExecuteQueryableCount(projected);
        if (count == 0)
        {
            value = null;
            return true;
        }

        if (count == 1)
        {
            value = 0d;
            return true;
        }

        try
        {
            var call = Expression.Call(
                method,
                Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                projected.Expression);
            var result = projected.Provider.Execute(call);
            value = result is null
                ? null
                : Convert.ToDouble(result, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private bool TryExecuteNativeStandardDeviation(
        IQueryable queryable,
        ScalarFieldModel scalarField,
        EfCoreProviderInfo providerInfo,
        bool population,
        out object? value)
    {
        value = null;

        if (!TryGetNativeStandardDeviationMethod(providerInfo.Family, scalarField.ClrType, population, out var method, out var projectionType))
        {
            return false;
        }

        if (!TryCreateQueryableProjection(queryable, scalarField, projectionType, out var projected))
        {
            return false;
        }

        var count = ExecuteQueryableCount(projected);
        if (count == 0)
        {
            value = null;
            return true;
        }

        if (count == 1)
        {
            value = 0d;
            return true;
        }

        try
        {
            var call = Expression.Call(
                method,
                Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                projected.Expression);
            var result = projected.Provider.Execute(call);
            value = result is null
                ? null
                : Convert.ToDouble(result, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static bool TryGetNativeStandardDeviationMethod(
        EfCoreProviderFamily family,
        Type clrType,
        bool population,
        out MethodInfo method,
        out Type projectionType)
    {
        var candidateProjectionType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        projectionType = candidateProjectionType;
        method = null!;

        if (!IsNativeAggregateNumericType(candidateProjectionType))
        {
            return false;
        }

        var (simpleTypeName, assemblyName) = family switch
        {
            EfCoreProviderFamily.SqlServer => ("SqlServerDbFunctionsExtensions", "Microsoft.EntityFrameworkCore.SqlServer"),
            EfCoreProviderFamily.PostgreSql => ("NpgsqlAggregateDbFunctionsExtensions", "Npgsql.EntityFrameworkCore.PostgreSQL"),
            EfCoreProviderFamily.Oracle => ("OracleDbFunctionsExtensions", "Oracle.EntityFrameworkCore"),
            _ => default
        };

        if (string.IsNullOrWhiteSpace(simpleTypeName) || string.IsNullOrWhiteSpace(assemblyName))
        {
            return false;
        }

        var methodName = population ? "StandardDeviationPopulation" : "StandardDeviationSample";
        method = GetProviderAggregateExtensionMethods(assemblyName, simpleTypeName)
            .FirstOrDefault(candidate => HasMatchingProviderAggregateMethodSignature(candidate, methodName, candidateProjectionType))!;
        return method is not null;
    }

    private static bool TryGetNativeVarianceMethod(
        EfCoreProviderFamily family,
        Type clrType,
        bool population,
        out MethodInfo method,
        out Type projectionType)
    {
        var candidateProjectionType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        projectionType = candidateProjectionType;
        method = null!;

        if (!IsNativeAggregateNumericType(candidateProjectionType))
        {
            return false;
        }

        var (simpleTypeName, assemblyName) = family switch
        {
            EfCoreProviderFamily.SqlServer => ("SqlServerDbFunctionsExtensions", "Microsoft.EntityFrameworkCore.SqlServer"),
            EfCoreProviderFamily.PostgreSql => ("NpgsqlAggregateDbFunctionsExtensions", "Npgsql.EntityFrameworkCore.PostgreSQL"),
            EfCoreProviderFamily.Oracle => ("OracleDbFunctionsExtensions", "Oracle.EntityFrameworkCore"),
            _ => default
        };

        if (string.IsNullOrWhiteSpace(simpleTypeName) || string.IsNullOrWhiteSpace(assemblyName))
        {
            return false;
        }

        var methodName = population ? "VariancePopulation" : "VarianceSample";
        method = GetProviderAggregateExtensionMethods(assemblyName, simpleTypeName)
            .FirstOrDefault(candidate => HasMatchingProviderAggregateMethodSignature(candidate, methodName, candidateProjectionType))!;
        return method is not null;
    }

    private static bool HasMatchingProviderAggregateMethodSignature(
        MethodInfo candidate,
        string methodName,
        Type candidateProjectionType)
    {
        if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
        {
            return false;
        }

        var parameters = candidate.GetParameters();
        if (parameters.Length != 2)
        {
            return false;
        }

        var selectorType = parameters[1].ParameterType;
        if (!selectorType.IsGenericType)
        {
            return false;
        }

        return selectorType.GetGenericArguments()[0] == candidateProjectionType;
    }

    private static Type? GetProviderAggregateExtensionsType(string assemblyName, string simpleTypeName)
    {
        try
        {
            var assembly = Assembly.Load(assemblyName);
            return assembly
                .GetTypes()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, simpleTypeName, StringComparison.Ordinal));
        }
        catch
        {
            return null;
        }
    }

    // Oracle remains in this lookup table because a future provider release may expose
    // public aggregate DbFunctions, but Oracle EF Core 9.23.x currently does not expose
    // native variance/stddev helpers, so Oracle falls through to the generic relational path.
    private static MethodInfo[] GetProviderAggregateExtensionMethods(string assemblyName, string simpleTypeName) =>
        GetProviderAggregateExtensionsType(assemblyName, simpleTypeName)?
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
        ?? [];

    private static bool IsNativeAggregateNumericType(Type type) =>
        type == typeof(byte) ||
        _nativeAggregateNumericTypes.Contains(type);

    private static readonly HashSet<Type> _nativeAggregateNumericTypes =
    [
        typeof(short),
        typeof(int),
        typeof(long),
        typeof(float),
        typeof(double),
        typeof(decimal)
    ];

    private bool TryCalculateQueryableMomentStatistics(
        IQueryable queryable,
        ScalarFieldModel scalarField,
        out QueryableMomentStatistics statistics)
    {
        statistics = default;

        if (!TryCreateQueryableProjection(queryable, scalarField, typeof(double), out var projected))
        {
            return false;
        }

        var values = projected.Cast<double>();

        try
        {
            var count = values.Count();
            if (count == 0)
            {
                statistics = new QueryableMomentStatistics(0, 0d, 0d, 0d, 0d);
                return true;
            }

            var raw1 = values.Average();
            var raw2 = values.Select(static value => value * value).Average();
            var raw3 = values.Select(static value => value * value * value).Average();
            var raw4 = values.Select(static value => value * value * value * value).Average();
            statistics = new QueryableMomentStatistics(count, raw1, raw2, raw3, raw4);
            return true;
        }
        catch
        {
            statistics = default;
            return false;
        }
    }

    private bool TryCreateQueryableProjection(
        IQueryable queryable,
        ScalarFieldModel scalarField,
        Type projectionType,
        out IQueryable projected)
    {
        projected = null!;

        var parameter = Expression.Parameter(queryable.ElementType, "row");
        var member = BuildMemberAccess(parameter, scalarField.Member);
        var memberType = Nullable.GetUnderlyingType(member.Type) ?? member.Type;

        if (!TypeInspection.IsNumeric(memberType))
        {
            return false;
        }

        try
        {
            Expression source = queryable.Expression;
            if (CanBeNull(member.Type))
            {
                var notNull = Expression.Lambda(
                    Expression.NotEqual(member, Expression.Constant(null, member.Type)),
                    parameter);

                source = Expression.Call(
                    typeof(Queryable),
                    nameof(Queryable.Where),
                    [queryable.ElementType],
                    source,
                    Expression.Quote(notNull));
            }

            var selectorBody = projectionType == member.Type
                ? member
                : Expression.Convert(member, projectionType);

            var selected = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Select),
                [queryable.ElementType, projectionType],
                source,
                Expression.Quote(Expression.Lambda(selectorBody, parameter)));

            projected = queryable.Provider.CreateQuery(selected);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetQueryableProjectedNumericValues(
        AggregateSelectionContext selection,
        string fieldName,
        out double[] values)
    {
        values = [];

        if (selection.IsFlat || selection.QueryableSource is not { } queryable)
        {
            return false;
        }

        if (_catalog.TryGetObjectType(selection.CollectionField.ElementType) is not { } model ||
            model.FindScalar(fieldName) is not { } scalarField ||
            !TryCreateQueryableProjection(queryable, scalarField, typeof(double), out var projected))
        {
            return false;
        }

        try
        {
            values = projected.Cast<double>().ToArray();
            return true;
        }
        catch
        {
            values = [];
            return false;
        }
    }

    private readonly record struct QueryableMomentStatistics(
        int Count,
        double Raw1,
        double Raw2,
        double Raw3,
        double Raw4)
    {
        public double? GetPopulationVariance()
        {
            if (Count == 0)
            {
                return null;
            }

            return ComputePopulationVariance();
        }

        public double? GetSampleVariance()
        {
            if (Count == 0)
            {
                return null;
            }

            if (Count == 1)
            {
                return 0d;
            }

            return ComputeSampleVariance();
        }

        public double? GetPopulationStandardDeviation()
        {
            if (Count == 0)
            {
                return null;
            }

            return Math.Sqrt(ComputePopulationVariance());
        }

        public double? GetSampleStandardDeviation()
        {
            if (Count == 0)
            {
                return null;
            }

            if (Count == 1)
            {
                return 0d;
            }

            return Math.Sqrt(ComputeSampleVariance());
        }

        public double? GetSkewness()
        {
            if (Count == 0)
            {
                return null;
            }

            var central2 = GetCentralMoment2();
            if (central2 <= 0d)
            {
                return 0d;
            }

            return GetCentralMoment3() / Math.Pow(central2, 1.5d);
        }

        public double? GetKurtosis()
        {
            if (Count == 0)
            {
                return null;
            }

            var central2 = GetCentralMoment2();
            if (central2 <= 0d)
            {
                return 0d;
            }

            return GetCentralMoment4() / (central2 * central2);
        }

        private double ComputePopulationVariance() => GetCentralMoment2();

        private double ComputeSampleVariance() => Math.Max(0d, GetCentralMoment2() * Count / (Count - 1d));

        private double GetCentralMoment2() => Math.Max(0d, Raw2 - (Raw1 * Raw1));

        private double GetCentralMoment3() =>
            Raw3 - (3d * Raw1 * Raw2) + (2d * Raw1 * Raw1 * Raw1);

        private double GetCentralMoment4() =>
            Raw4 - (4d * Raw1 * Raw3) + (6d * Raw1 * Raw1 * Raw2) - (3d * Raw1 * Raw1 * Raw1 * Raw1);
    }

    private object? ExecuteQueryableMinOrMax(IQueryable queryable, ScalarFieldModel scalarField, bool min)
    {
        var parameter = Expression.Parameter(queryable.ElementType, "row");
        var member = BuildMemberAccess(parameter, scalarField.Member);

        Expression source = queryable.Expression;
        if (CanBeNull(member.Type))
        {
            var notNull = Expression.Lambda(
                Expression.NotEqual(member, Expression.Constant(null, member.Type)),
                parameter);

            source = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Where),
                [queryable.ElementType],
                source,
                Expression.Quote(notNull));
        }

        var selected = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            [queryable.ElementType, member.Type],
            source,
            Expression.Quote(Expression.Lambda(member, parameter)));

        var any = Expression.Call(typeof(Queryable), nameof(Queryable.Any), [member.Type], selected);
        if (!(bool)queryable.Provider.Execute(any)!)
        {
            return null;
        }

        try
        {
            var methodName = min ? nameof(Queryable.Min) : nameof(Queryable.Max);
            var aggregateCall = Expression.Call(typeof(Queryable), methodName, [member.Type], selected);
            return queryable.Provider.Execute(aggregateCall);
        }
        catch
        {
            return UnsupportedAggregateValue.Instance;
        }
    }

    private bool TryExecuteQueryableDistinctKeys(
        IQueryable queryable,
        IReadOnlyList<ScalarFieldModel> keyFields,
        out IReadOnlyList<object?> keys)
    {
        keys = [];

        var parameter = Expression.Parameter(queryable.ElementType, "row");
        var keySelector = BuildKeySelectorLambda(queryable.ElementType, keyFields, parameter, out var keyType);

        try
        {
            var selected = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Select),
                [queryable.ElementType, keyType],
                queryable.Expression,
                Expression.Quote(keySelector));

            var distinct = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Distinct),
                [keyType],
                selected);

            var distinctQuery = queryable.Provider.CreateQuery(distinct);
            keys = distinctQuery.Cast<object?>().ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private IQueryable ApplyQueryableKeyFilter(
        IQueryable queryable,
        IReadOnlyList<ScalarFieldModel> keyFields,
        object? keyValue)
    {
        var values = ExtractKeyValues(keyValue, keyFields.Count);
        var parameter = Expression.Parameter(queryable.ElementType, "row");
        var parts = new List<Expression>(keyFields.Count);

        for (var index = 0; index < keyFields.Count; index++)
        {
            var member = BuildMemberAccess(parameter, keyFields[index].Member);
            TryCreateTypedConstant(member.Type, values[index], out var constant);
            parts.Add(Expression.Equal(member, constant));
        }

        var predicate = CombineAnd(parts) ?? Expression.Constant(true);
        var where = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Where),
            [queryable.ElementType],
            queryable.Expression,
            Expression.Quote(Expression.Lambda(predicate, parameter)));

        return queryable.Provider.CreateQuery(where);
    }

    private IReadOnlyDictionary<string, object?> CreateKeyDictionary(
        IReadOnlyList<ScalarFieldModel> keyFields,
        object? keyValue)
    {
        var values = ExtractKeyValues(keyValue, keyFields.Count);
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);

        for (var index = 0; index < keyFields.Count; index++)
        {
            dictionary[keyFields[index].GraphQlName] = values[index];
        }

        return dictionary;
    }

    private static object?[] ExtractKeyValues(object? keyValue, int expectedCount)
    {
        if (expectedCount == 1)
        {
            return [keyValue];
        }

        var values = new List<object?>(expectedCount);
        UnpackTupleValues(keyValue, values);
        return values.Take(expectedCount).ToArray();
    }

    private static void UnpackTupleValues(object? tuple, List<object?> values)
    {
        if (tuple is null)
        {
            values.Add(null);
            return;
        }

        var type = tuple.GetType();
        if (!type.FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
        {
            values.Add(tuple);
            return;
        }

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (var field in fields)
        {
            var fieldValue = field.GetValue(tuple);
            if (field.Name == "Rest")
            {
                UnpackTupleValues(fieldValue, values);
            }
            else
            {
                values.Add(fieldValue);
            }
        }
    }

    private static LambdaExpression BuildKeySelectorLambda(
        Type elementType,
        IReadOnlyList<ScalarFieldModel> keyFields,
        ParameterExpression parameter,
        out Type keyType)
    {
        var members = keyFields
            .Select(field => BuildMemberAccess(parameter, field.Member))
            .ToArray();

        var keyExpression = BuildCompositeKeyExpression(members, out keyType);
        return Expression.Lambda(keyExpression, parameter);
    }

    private static Expression BuildCompositeKeyExpression(
        IReadOnlyList<Expression> values,
        out Type keyType)
    {
        if (values.Count == 1)
        {
            keyType = values[0].Type;
            return values[0];
        }

        if (values.Count > 7)
        {
            throw new NotSupportedException("Queryable grouping supports up to seven key fields.");
        }

        var tupleDefinition = Type.GetType($"System.ValueTuple`{values.Count}", throwOnError: true)!;
        var tupleTypes = values.Select(value => value.Type).ToArray();
        keyType = tupleDefinition.MakeGenericType(tupleTypes);
        var constructor = keyType.GetConstructors().Single();

        return Expression.New(constructor, values);
    }

    private static Expression EnsureEnumerableCollection(Expression collectionExpression, Type elementType)
    {
        var targetType = typeof(IEnumerable<>).MakeGenericType(elementType);
        var converted = collectionExpression.Type == targetType
            ? collectionExpression
            : Expression.Convert(collectionExpression, targetType);

        if (!CanBeNull(collectionExpression.Type))
        {
            return converted;
        }

        var emptyArray = Array.CreateInstance(elementType, 0);
        var emptyEnumerable = Expression.Convert(Expression.Constant(emptyArray, emptyArray.GetType()), targetType);
        return Expression.Coalesce(converted, emptyEnumerable);
    }

    private bool TryBuildQueryableAggregateValueExpression(
        CollectionFieldModel collectionField,
        Expression sequence,
        string fieldName,
        AggregateOperator aggregateOperator,
        out Expression expression)
    {
        expression = null!;

        if (_catalog.TryGetObjectType(collectionField.ElementType) is not { } model ||
            model.FindScalar(fieldName) is not { } scalarField)
        {
            return false;
        }

        var parameter = Expression.Parameter(collectionField.ElementType, "item");
        var member = BuildMemberAccess(parameter, scalarField.Member);
        var selected = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Select),
            [collectionField.ElementType, member.Type],
            sequence,
            Expression.Lambda(member, parameter));

        switch (aggregateOperator)
        {
            case AggregateOperator.CountDistinct:
                if (CanBeNull(member.Type))
                {
                    var notNullPredicate = Expression.Lambda(
                        Expression.NotEqual(member, Expression.Constant(null, member.Type)),
                        parameter);

                    selected = Expression.Call(
                        typeof(Enumerable),
                        nameof(Enumerable.Where),
                        [collectionField.ElementType],
                        sequence,
                        notNullPredicate);

                    selected = Expression.Call(
                        typeof(Enumerable),
                        nameof(Enumerable.Select),
                        [collectionField.ElementType, member.Type],
                        selected,
                        Expression.Lambda(member, parameter));
                }

                var distinct = Expression.Call(
                    typeof(Enumerable),
                    nameof(Enumerable.Distinct),
                    [member.Type],
                    selected);

                expression = Expression.Call(
                    typeof(Enumerable),
                    nameof(Enumerable.Count),
                    [member.Type],
                    distinct);
                return true;

            case AggregateOperator.Sum:
            case AggregateOperator.Avg:
                if (!TypeInspection.IsNumeric(scalarField.ClrType))
                {
                    return false;
                }

                var numericProjection = Expression.Call(
                    typeof(Enumerable),
                    nameof(Enumerable.Select),
                    [collectionField.ElementType, typeof(double?)],
                    sequence,
                    Expression.Lambda(Expression.Convert(member, typeof(double?)), parameter));

                expression = Expression.Call(
                    typeof(Enumerable),
                    aggregateOperator == AggregateOperator.Sum ? nameof(Enumerable.Sum) : nameof(Enumerable.Average),
                    Type.EmptyTypes,
                    numericProjection);
                return true;

            case AggregateOperator.Var:
            case AggregateOperator.Varp:
                if (!TypeInspection.IsNumeric(scalarField.ClrType))
                {
                    return false;
                }

                expression = BuildEnumerableVarianceExpression(
                    sequence,
                    parameter,
                    member,
                    collectionField.ElementType,
                    aggregateOperator == AggregateOperator.Varp);
                return true;

            case AggregateOperator.Min:
            case AggregateOperator.Max:
                Expression minMaxSequence = selected;

                if (CanBeNull(member.Type))
                {
                    var notNullPredicate = Expression.Lambda(
                        Expression.NotEqual(member, Expression.Constant(null, member.Type)),
                        parameter);

                    var filteredSequence = Expression.Call(
                        typeof(Enumerable),
                        nameof(Enumerable.Where),
                        [collectionField.ElementType],
                        sequence,
                        notNullPredicate);

                    minMaxSequence = Expression.Call(
                        typeof(Enumerable),
                        nameof(Enumerable.Select),
                        [collectionField.ElementType, member.Type],
                        filteredSequence,
                        Expression.Lambda(member, parameter));
                }

                expression = Expression.Call(
                    typeof(Enumerable),
                    aggregateOperator == AggregateOperator.Min ? nameof(Enumerable.Min) : nameof(Enumerable.Max),
                    [member.Type],
                    minMaxSequence);
                return true;

            default:
                return false;
        }
    }

    private static Expression BuildEnumerableVarianceExpression(
        Expression sequence,
        ParameterExpression parameter,
        Expression member,
        Type elementType,
        bool population)
    {
        Expression filteredSequence = sequence;
        if (CanBeNull(member.Type))
        {
            var notNullPredicate = Expression.Lambda(
                Expression.NotEqual(member, Expression.Constant(null, member.Type)),
                parameter);

            filteredSequence = Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Where),
                [elementType],
                sequence,
                notNullPredicate);
        }

        var doubleProjection = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Select),
            [elementType, typeof(double)],
            filteredSequence,
            Expression.Lambda(Expression.Convert(member, typeof(double)), parameter));

        var count = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Count),
            [elementType],
            filteredSequence);
        var countDouble = Expression.Convert(count, typeof(double));

        var raw1 = Expression.Call(typeof(Enumerable), nameof(Enumerable.Average), Type.EmptyTypes, doubleProjection);

        var squaredProjectionParameter = Expression.Parameter(typeof(double), "value");
        var squaredProjection = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Select),
            [typeof(double), typeof(double)],
            doubleProjection,
            Expression.Lambda(
                Expression.Multiply(squaredProjectionParameter, squaredProjectionParameter),
                squaredProjectionParameter));
        var raw2 = Expression.Call(typeof(Enumerable), nameof(Enumerable.Average), Type.EmptyTypes, squaredProjection);

        var central2 = Expression.Subtract(raw2, Expression.Multiply(raw1, raw1));
        var variance = population
            ? central2
            : Expression.Multiply(central2, Expression.Divide(countDouble, Expression.Subtract(countDouble, Expression.Constant(1d))));
        var clampedVariance = Expression.Condition(
            Expression.LessThan(variance, Expression.Constant(0d)),
            Expression.Constant(0d),
            variance);

        var nullResult = Expression.Constant(null, typeof(double?));
        var zeroResult = Expression.Constant(0d, typeof(double?));
        var varianceResult = Expression.Convert(clampedVariance, typeof(double?));

        return population
            ? Expression.Condition(
                Expression.Equal(count, Expression.Constant(0)),
                nullResult,
                varianceResult)
            : Expression.Condition(
                Expression.Equal(count, Expression.Constant(0)),
                nullResult,
                Expression.Condition(
                    Expression.Equal(count, Expression.Constant(1)),
                    zeroResult,
                    varianceResult));
    }

    private static bool TryParseSupportedQueryableAggregateOperator(string name, out AggregateOperator aggregateOperator)
    {
        switch (name)
        {
            case "countDistinct":
                aggregateOperator = AggregateOperator.CountDistinct;
                return true;
            case "sum":
                aggregateOperator = AggregateOperator.Sum;
                return true;
            case "avg":
                aggregateOperator = AggregateOperator.Avg;
                return true;
            case "var":
                aggregateOperator = AggregateOperator.Var;
                return true;
            case "varp":
                aggregateOperator = AggregateOperator.Varp;
                return true;
            case "min":
                aggregateOperator = AggregateOperator.Min;
                return true;
            case "max":
                aggregateOperator = AggregateOperator.Max;
                return true;
            default:
                aggregateOperator = default;
                return false;
        }
    }

    private static Expression BuildMemberAccess(Expression instance, MemberInfo member) =>
        member switch
        {
            PropertyInfo property => Expression.Property(instance, property),
            FieldInfo field => Expression.Field(instance, field),
            _ => throw new NotSupportedException($"Unsupported member {member}.")
        };

    private static bool CanUseOrderedComparison(Type type)
    {
        var actualType = Nullable.GetUnderlyingType(type) ?? type;

        return TypeInspection.IsNumeric(actualType)
            || actualType == typeof(DateOnly)
            || actualType == typeof(DateTime)
            || actualType == typeof(string);
    }

    private static bool CanBeNull(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private sealed class UnsupportedAggregateValue
    {
        public static UnsupportedAggregateValue Instance { get; } = new();
    }

    private static readonly MethodInfo _wrapQueryableExecutableMethod = typeof(CollectionExecutionEngine)
        .GetMethod(nameof(WrapQueryableExecutable), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo _wrapEnumerableExecutableMethod = typeof(CollectionExecutionEngine)
        .GetMethod(nameof(WrapEnumerableExecutable), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static object WrapQueryableExecutable<T>(IQueryable<T> source) => source.AsExecutable();

    private static object WrapEnumerableExecutable<T>(IEnumerable<T> source) => source.AsExecutable();
}
