using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Extension.CollectionEnhancements.Metadata;

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
            return queryableResult!;
        }

        return ApplyCollectionArguments(collectionField, sourceValue, where, order, offset, limit);
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

            if (model.CollectionFields.FirstOrDefault(field => field.AggregateFieldName == name) is not null ||
                model.CollectionFields.FirstOrDefault(field => field.GroupFieldName == name) is not null)
            {
                if (InputValueNormalizer.AsDictionary(value).Count == 0)
                {
                    continue;
                }

                expression = null;
                return false;
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

            if (child is null)
            {
                if (isOr)
                {
                    expression = Expression.Constant(true);
                    return true;
                }

                continue;
            }

            parts.Add(child);
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
}
