using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Extension.CollectionEnhancements.Execution;
using HotChocolate.Extension.CollectionEnhancements.Metadata;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestData;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

public sealed class QueryableAggregateGroupCoverageTests
{
    private static readonly CollectionSchemaCatalog Catalog = CollectionSchemaCatalog.CreateDefault();
    private static readonly CollectionExecutionEngine Engine = new(Catalog);

    [Fact]
    public void AggregateSelectionContext_ShouldLazyMaterializeQueryableRows_AndCacheResults()
    {
        var ordersField = GetCollectionField<Customer>("orders");
        var queryable = ExampleData.Customers[0].Orders.AsQueryable();
        var selection = new AggregateSelectionContext(ordersField, isFlat: false, queryable);

        Assert.False(selection.IsFlat);
        Assert.Equal(string.Empty, selection.ResultPrefix);
        Assert.Same(queryable, selection.QueryableSource);
        Assert.False(selection.TryGetCachedAggregate("missing", out _));

        Assert.Equal(42d, selection.GetOrAddCachedAggregate("sum:total", static () => 42d));
        Assert.True(selection.TryGetCachedAggregate("sum:total", out var cached));
        Assert.Equal(42d, cached);
        Assert.Equal(42d, selection.GetOrAddCachedAggregate("sum:total", static () => -1d));

        Assert.Equal(4, selection.Rows.Count);
        Assert.Equal(4, selection.Rows.Count);

        var flatSelection = new AggregateSelectionContext(ordersField, isFlat: true, ExampleData.Customers[0].Orders.Cast<object>().ToArray());
        Assert.Equal("Flat", flatSelection.ResultPrefix);
        Assert.Null(flatSelection.QueryableSource);
    }

    [Fact]
    public void QueryableAggregateExecution_ShouldCoverProviderAwareAndFallbackBranches()
    {
        var ordersField = GetCollectionField<Customer>("orders");
        var ordersModel = Catalog.TryGetObjectType(typeof(Order))!;
        var totalScalar = ordersModel.FindScalar("total")!;
        var referenceScalar = ordersModel.FindScalar("reference")!;
        var couponModel = Catalog.TryGetObjectType(typeof(Coupon))!;
        var interestRateScalar = couponModel.FindScalar("interestRate")!;
        var queryableOrders = ExampleData.Customers[0].Orders.AsQueryable();

        Assert.Equal(4, ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ExecuteQueryableCount", queryableOrders));

        var aggregateSelection = Engine.CreateAggregateSelection(
            ordersField,
            queryableOrders,
            new Dictionary<string, object?>
            {
                ["status"] = new Dictionary<string, object?> { ["eq"] = "Active" }
            },
            having: null,
            isFlat: false,
            expand: null);
        Assert.NotNull(aggregateSelection);
        Assert.NotNull(aggregateSelection.QueryableSource);

        Assert.Null(Engine.CreateAggregateSelection(
            ordersField,
            queryableOrders,
            where: null,
            having: new Dictionary<string, object?>
            {
                ["count"] = new Dictionary<string, object?> { ["gt"] = 10 }
            },
            isFlat: false,
            expand: null));

        var invalidQueryableSelection = InvokeInstanceWithArguments(
            Engine,
            "TryCreateQueryableAggregateSelection",
            ordersField,
            queryableOrders,
            new Dictionary<string, object?>
            {
                ["total"] = new Dictionary<string, object?> { ["gt"] = "bad" }
            },
            null);
        Assert.False((bool)invalidQueryableSelection.ReturnValue!);
        Assert.Null(invalidQueryableSelection.Arguments[3]);

        var nonQueryableSelection = InvokeInstanceWithArguments(
            Engine,
            "TryCreateQueryableAggregateSelection",
            ordersField,
            ExampleData.Customers[0].Orders,
            null,
            null);
        Assert.False((bool)nonQueryableSelection.ReturnValue!);

        var selection = new AggregateSelectionContext(ordersField, isFlat: false, queryableOrders);
        Assert.Equal(4, Engine.ResolveCount(selection, where: null));
        Assert.Equal(
            3,
            Engine.ResolveCount(
                selection,
                new Dictionary<string, object?>
                {
                    ["status"] = new Dictionary<string, object?> { ["eq"] = "Active" }
                }));
        Assert.Equal(
            4,
            Engine.ResolveCount(
                selection,
                new Dictionary<string, object?>
                {
                    ["missing"] = new Dictionary<string, object?> { ["eq"] = 1 }
                }));

        Assert.Equal(
            1_600d,
            Engine.ResolveAggregateProjectionField(
                new AggregateProjection(selection, AggregateOperator.Sum, null, null),
                "total"));
        Assert.Equal(
            400d,
            Engine.ResolveAggregateProjectionField(
                new AggregateProjection(selection, AggregateOperator.Avg, null, null),
                "total"));
        Assert.Equal(
            38_333.333333333336d,
            Convert.ToDouble(
                Engine.ResolveAggregateProjectionField(
                    new AggregateProjection(selection, AggregateOperator.Var, null, null),
                    "total")),
            12);
        Assert.Equal(
            28_750d,
            Convert.ToDouble(
                Engine.ResolveAggregateProjectionField(
                    new AggregateProjection(selection, AggregateOperator.Varp, null, null),
                    "total")),
            12);
        Assert.Equal(
            3,
            Engine.ResolveAggregateProjectionField(
                new AggregateProjection(selection, AggregateOperator.CountDistinct, null, null),
                "reference"));
        Assert.Equal(
            200m,
            Engine.ResolveAggregateProjectionField(
                new AggregateProjection(selection, AggregateOperator.Min, null, null),
                "total"));
        Assert.Equal(
            650m,
            Engine.ResolveAggregateProjectionField(
                new AggregateProjection(selection, AggregateOperator.Max, null, null),
                "total"));

        var resolveSum = InvokeInstanceWithArguments(
            Engine,
            "TryResolveQueryableAggregate",
            selection,
            "total",
            AggregateOperator.Sum,
            null);
        Assert.True((bool)resolveSum.ReturnValue!);
        Assert.Equal(1_600d, resolveSum.Arguments[3]);

        var resolveMissingField = InvokeInstanceWithArguments(
            Engine,
            "TryResolveQueryableAggregate",
            selection,
            "missing",
            AggregateOperator.Sum,
            null);
        Assert.False((bool)resolveMissingField.ReturnValue!);
        Assert.Null(resolveMissingField.Arguments[3]);

        var resolveUnsupportedOperator = InvokeInstanceWithArguments(
            Engine,
            "TryResolveQueryableAggregate",
            selection,
            "total",
            AggregateOperator.Stdev,
            null);
        Assert.False((bool)resolveUnsupportedOperator.ReturnValue!);
        Assert.Null(resolveUnsupportedOperator.Arguments[3]);

        var flatSelection = new AggregateSelectionContext(ordersField, isFlat: true, ExampleData.Customers[0].Orders.Cast<object>().ToArray());
        var resolveFlat = InvokeInstanceWithArguments(
            Engine,
            "TryResolveQueryableAggregate",
            flatSelection,
            "total",
            AggregateOperator.Sum,
            null);
        Assert.False((bool)resolveFlat.ReturnValue!);

        var stringRowsField = new CollectionFieldModel(
            typeof(StringRowsHost).GetProperty(nameof(StringRowsHost.Rows))!,
            "rows",
            typeof(string[]),
            typeof(string),
            "Query",
            "String");
        var noModelSelection = new AggregateSelectionContext(stringRowsField, isFlat: false, new[] { "A", "B" }.AsQueryable());
        var resolveNoModel = InvokeInstanceWithArguments(
            Engine,
            "TryResolveQueryableAggregate",
            noModelSelection,
            "value",
            AggregateOperator.Sum,
            null);
        Assert.False((bool)resolveNoModel.ReturnValue!);

        var unsupportedAggregateValue = ReflectionTestSupport.GetNestedPrivateType<object>(
            typeof(CollectionExecutionEngine),
            "UnsupportedAggregateValue",
            "Instance");

        Assert.Same(
            unsupportedAggregateValue,
            ReflectionTestSupport.InvokeInstance(
                Engine,
                "ExecuteQueryableNumericAggregate",
                queryableOrders,
                referenceScalar,
                nameof(Queryable.Sum)));

        Assert.Null(ReflectionTestSupport.InvokeInstance(
            Engine,
            "ExecuteQueryableNumericAggregate",
            Array.Empty<Order>().AsQueryable(),
            totalScalar,
            nameof(Queryable.Average)));

        Assert.Equal(
            3,
            ReflectionTestSupport.InvokeInstance(
                Engine,
                "ExecuteQueryableCountDistinct",
                ExampleData.Securities[2].Details.Coupons.AsQueryable(),
                interestRateScalar));

        Assert.Null(ReflectionTestSupport.InvokeInstance(
            Engine,
            "ExecuteQueryableMinOrMax",
            Array.Empty<Order>().AsQueryable(),
            totalScalar,
            true));

        Assert.Equal(
            0.015m,
            ReflectionTestSupport.InvokeInstance(
                Engine,
                "ExecuteQueryableMinOrMax",
                ExampleData.Securities[2].Details.Coupons.AsQueryable(),
                interestRateScalar,
                true));

        var minFailureProvider = new InterceptingQueryable<Order>(ExampleData.Customers[0].Orders.AsQueryable(), ["Min"]);
        Assert.Same(
            unsupportedAggregateValue,
            ReflectionTestSupport.InvokeInstance(
                Engine,
                "ExecuteQueryableMinOrMax",
                minFailureProvider,
                totalScalar,
                true));
    }

    [Fact]
    public void QueryableGroupExecution_ShouldCoverGroupingKeyAndDistinctBranches()
    {
        var ordersField = GetCollectionField<Customer>("orders");
        var groupedRows = Engine.CreateGroupRows(
            ordersField,
            ExampleData.Customers[0].Orders.AsQueryable(),
            by: new object?[] { "status", "reference" },
            where: null,
            having: null,
            order: new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["key"] = new Dictionary<string, object?> { ["reference"] = "ASC" }
                }
            },
            offset: 1,
            limit: 2,
            isFlat: false,
            expand: null);

        Assert.Equal(2, groupedRows.Count);
        Assert.All(groupedRows, group => Assert.NotNull(group.Selection.QueryableSource));

        var stringRowsField = new CollectionFieldModel(
            typeof(StringRowsHost).GetProperty(nameof(StringRowsHost.Rows))!,
            "rows",
            typeof(string[]),
            typeof(string),
            "Query",
            "String");

        var noModelGroups = InvokeInstanceWithArguments(
            Engine,
            "TryCreateQueryableGroupRows",
            stringRowsField,
            new[] { "A", "B" }.AsQueryable(),
            new[] { "value" },
            null,
            null);
        Assert.False((bool)noModelGroups.ReturnValue!);

        var missingScalarGroups = InvokeInstanceWithArguments(
            Engine,
            "TryCreateQueryableGroupRows",
            ordersField,
            ExampleData.Customers[0].Orders.AsQueryable(),
            new[] { "missing" },
            null,
            null);
        Assert.False((bool)missingScalarGroups.ReturnValue!);

        var invalidWhereGroups = InvokeInstanceWithArguments(
            Engine,
            "TryCreateQueryableGroupRows",
            ordersField,
            ExampleData.Customers[0].Orders.AsQueryable(),
            new[] { "status" },
            new Dictionary<string, object?>
            {
                ["total"] = new Dictionary<string, object?> { ["gt"] = "bad" }
            },
            null);
        Assert.False((bool)invalidWhereGroups.ReturnValue!);

        var throwingDistinctGroups = InvokeInstanceWithArguments(
            Engine,
            "TryCreateQueryableGroupRows",
            ordersField,
            new InterceptingQueryable<Order>(ExampleData.Customers[0].Orders.AsQueryable(), ["Distinct"]),
            new[] { "status" },
            null,
            null);
        Assert.False((bool)throwingDistinctGroups.ReturnValue!);

        var orderModel = Catalog.TryGetObjectType(typeof(Order))!;
        var keyFields = new[]
        {
            orderModel.FindScalar("status")!,
            orderModel.FindScalar("reference")!
        };

        var distinctKeys = InvokeInstanceWithArguments(
            Engine,
            "TryExecuteQueryableDistinctKeys",
            ExampleData.Customers[0].Orders.AsQueryable(),
            keyFields,
            null);
        Assert.True((bool)distinctKeys.ReturnValue!);
        Assert.Equal(3, ((IReadOnlyList<object?>)distinctKeys.Arguments[2]!).Count);

        var failedDistinctKeys = InvokeInstanceWithArguments(
            Engine,
            "TryExecuteQueryableDistinctKeys",
            new InterceptingQueryable<Order>(ExampleData.Customers[0].Orders.AsQueryable(), ["Distinct"]),
            keyFields,
            null);
        Assert.False((bool)failedDistinctKeys.ReturnValue!);

        var activeAlphaKey = (OrderStatus.Active, "ORD-ALPHA");
        var filteredQuery = Assert.IsAssignableFrom<IQueryable>(ReflectionTestSupport.InvokeInstance(
            Engine,
            "ApplyQueryableKeyFilter",
            ExampleData.Customers[0].Orders.AsQueryable(),
            keyFields,
            activeAlphaKey));
        Assert.Equal([1001, 1003], filteredQuery.Cast<Order>().Select(order => order.Id).ToArray());

        var unfilteredQuery = Assert.IsAssignableFrom<IQueryable>(ReflectionTestSupport.InvokeInstance(
            Engine,
            "ApplyQueryableKeyFilter",
            ExampleData.Customers[0].Orders.AsQueryable(),
            Array.Empty<ScalarFieldModel>(),
            null));
        Assert.Equal(4, unfilteredQuery.Cast<Order>().Count());

        var keyDictionary = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(ReflectionTestSupport.InvokeInstance(
            Engine,
            "CreateKeyDictionary",
            keyFields,
            activeAlphaKey));
        Assert.Equal(OrderStatus.Active, keyDictionary["status"]);
        Assert.Equal("ORD-ALPHA", keyDictionary["reference"]);

        var singleKeyValues = Assert.IsType<object?[]>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ExtractKeyValues",
            "single",
            1));
        Assert.Equal(["single"], singleKeyValues);

        var nullExpanded = Assert.IsType<object?[]>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ExtractKeyValues",
            null,
            2));
        Assert.Equal([null], nullExpanded);

        var plainExpanded = Assert.IsType<object?[]>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ExtractKeyValues",
            "plain",
            2));
        Assert.Equal(["plain"], plainExpanded);

        object nestedTuple = (1, 2, 3, 4, 5, 6, 7, 8, 9);
        var nestedTupleValues = Assert.IsType<object?[]>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ExtractKeyValues",
            nestedTuple,
            9));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], nestedTupleValues);

        var singleKeyExpression = InvokeStaticWithArguments(
            typeof(CollectionExecutionEngine),
            "BuildCompositeKeyExpression",
            new Expression[] { Expression.Constant(1) },
            null);
        Assert.IsType<ConstantExpression>(singleKeyExpression.ReturnValue);
        Assert.Equal(typeof(int), singleKeyExpression.Arguments[1]);

        var tooManyKeys = Assert.Throws<TargetInvocationException>(() =>
            InvokeStaticWithArguments(
                typeof(CollectionExecutionEngine),
                "BuildCompositeKeyExpression",
                Enumerable.Range(0, 8).Select(value => Expression.Constant(value)).ToArray(),
                null));
        Assert.Contains("up to seven key fields", tooManyKeys.InnerException!.Message, StringComparison.Ordinal);

        var owner = Expression.Parameter(typeof(NullableOrdersHost), "owner");
        var nullableCollection = Assert.IsAssignableFrom<Expression>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "EnsureEnumerableCollection",
            Expression.Property(owner, nameof(NullableOrdersHost.Orders)),
            typeof(Order)));
        Assert.Equal(ExpressionType.Coalesce, nullableCollection.NodeType);

        var alreadyEnumerableCollection = Assert.IsAssignableFrom<Expression>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "EnsureEnumerableCollection",
            Expression.Parameter(typeof(IEnumerable<Order>), "orders"),
            typeof(Order)));
        Assert.NotEqual(ExpressionType.Convert, alreadyEnumerableCollection.NodeType);

        var nonNullableCollection = Assert.IsAssignableFrom<Expression>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "EnsureEnumerableCollection",
            Expression.Property(owner, nameof(NullableOrdersHost.RequiredOrderStruct)),
            typeof(Order)));
        Assert.NotEqual(ExpressionType.Coalesce, nonNullableCollection.NodeType);
    }

    [Fact]
    public void QueryableCriteriaAndHavingHelpers_ShouldCoverRemainingHelperBranches()
    {
        var customerParameter = Expression.Parameter(typeof(Customer), "customer");
        var ordersField = GetCollectionField<Customer>("orders");
        var couponsField = GetCollectionField<SecurityDetails>("coupons");
        var stringRowsField = new CollectionFieldModel(
            typeof(StringRowsHost).GetProperty(nameof(StringRowsHost.Rows))!,
            "rows",
            typeof(string[]),
            typeof(string),
            "Query",
            "String");

        var emptyAggregateCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildAggregateCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>(),
            null);
        Assert.True((bool)emptyAggregateCriteria.ReturnValue!);
        Assert.Null(emptyAggregateCriteria.Arguments[3]);

        var missingHavingAggregateCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildAggregateCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["where"] = new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "Active" } }
            },
            null);
        Assert.True((bool)missingHavingAggregateCriteria.ReturnValue!);
        Assert.Null(missingHavingAggregateCriteria.Arguments[3]);

        var invalidWhereAggregateCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildAggregateCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["where"] = new Dictionary<string, object?> { ["total"] = new Dictionary<string, object?> { ["gt"] = "bad" } },
                ["having"] = new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gt"] = 1 } }
            },
            null);
        Assert.False((bool)invalidWhereAggregateCriteria.ReturnValue!);

        var aggregateCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildAggregateCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["where"] = new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "Active" } },
                ["having"] = new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gt"] = 1 } }
            },
            null);
        Assert.True((bool)aggregateCriteria.ReturnValue!);
        Assert.IsAssignableFrom<Expression>(aggregateCriteria.Arguments[3]);

        var objectFilterWithInvalidGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Customer),
            customerParameter,
            new Dictionary<string, object?>
            {
                ["ordersGroup"] = new Dictionary<string, object?>
                {
                    ["order"] = new object?[] { new Dictionary<string, object?> { ["count"] = "DESC" } }
                }
            },
            null);
        Assert.True((bool)objectFilterWithInvalidGroupCriteria.ReturnValue!);
        Assert.IsType<ConstantExpression>(objectFilterWithInvalidGroupCriteria.Arguments[3]);

        var objectFilterWithFailingGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Customer),
            customerParameter,
            new Dictionary<string, object?>
            {
                ["ordersGroup"] = new Dictionary<string, object?>
                {
                    ["by"] = new object?[] { "status" },
                    ["order"] = new object?[] { new Dictionary<string, object?> { ["count"] = "DESC" } }
                }
            },
            null);
        Assert.False((bool)objectFilterWithFailingGroupCriteria.ReturnValue!);

        var emptyGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>(),
            null);
        Assert.True((bool)emptyGroupCriteria.ReturnValue!);
        Assert.Null(emptyGroupCriteria.Arguments[3]);

        var missingByGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?> { ["by"] = Array.Empty<object>() },
            null);
        Assert.True((bool)missingByGroupCriteria.ReturnValue!);
        Assert.IsType<ConstantExpression>(missingByGroupCriteria.Arguments[3]);

        var noByPropertyGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["having"] = new Dictionary<string, object?>
                {
                    ["count"] = new Dictionary<string, object?> { ["gt"] = 1 }
                }
            },
            null);
        Assert.True((bool)noByPropertyGroupCriteria.ReturnValue!);
        Assert.IsType<ConstantExpression>(noByPropertyGroupCriteria.Arguments[3]);

        var invalidOrderedGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["by"] = new object?[] { "status" },
                ["order"] = new object?[] { new Dictionary<string, object?> { ["count"] = "DESC" } }
            },
            null);
        Assert.False((bool)invalidOrderedGroupCriteria.ReturnValue!);

        var invalidOffsetGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["by"] = new object?[] { "status" },
                ["offset"] = 1
            },
            null);
        Assert.False((bool)invalidOffsetGroupCriteria.ReturnValue!);

        var invalidLimitGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["by"] = new object?[] { "status" },
                ["limit"] = 1
            },
            null);
        Assert.False((bool)invalidLimitGroupCriteria.ReturnValue!);

        var noModelGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            Expression.Parameter(typeof(StringRowsHost), "host"),
            stringRowsField,
            new Dictionary<string, object?>
            {
                ["by"] = new object?[] { "value" }
            },
            null);
        Assert.False((bool)noModelGroupCriteria.ReturnValue!);

        var missingScalarGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["by"] = new object?[] { "missing" }
            },
            null);
        Assert.False((bool)missingScalarGroupCriteria.ReturnValue!);

        var invalidWhereGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["by"] = new object?[] { "status" },
                ["where"] = new Dictionary<string, object?> { ["total"] = new Dictionary<string, object?> { ["gt"] = "bad" } }
            },
            null);
        Assert.False((bool)invalidWhereGroupCriteria.ReturnValue!);

        var anyGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["by"] = new object?[] { "status" }
            },
            null);
        Assert.True((bool)anyGroupCriteria.ReturnValue!);
        Assert.IsAssignableFrom<MethodCallExpression>(anyGroupCriteria.Arguments[3]);

        var sparseByGroupCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["by"] = new object?[] { null, "status" }
            },
            null);
        Assert.True((bool)sparseByGroupCriteria.ReturnValue!);
        Assert.IsAssignableFrom<MethodCallExpression>(sparseByGroupCriteria.Arguments[3]);

        var nullPredicateHavingCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["by"] = new object?[] { "status" },
                ["having"] = new Dictionary<string, object?>
                {
                    ["sum"] = new Dictionary<string, object?>
                    {
                        ["total"] = null
                    }
                }
            },
            null);
        Assert.True((bool)nullPredicateHavingCriteria.ReturnValue!);
        Assert.IsAssignableFrom<MethodCallExpression>(nullPredicateHavingCriteria.Arguments[3]);

        var invalidHavingCriteria = InvokeInstanceWithArguments(
            Engine,
            "TryBuildGroupCriteriaExpression",
            customerParameter,
            ordersField,
            new Dictionary<string, object?>
            {
                ["by"] = new object?[] { "status" },
                ["having"] = new Dictionary<string, object?>
                {
                    ["stdev"] = new Dictionary<string, object?>
                    {
                        ["total"] = new Dictionary<string, object?> { ["gt"] = 1 }
                    }
                }
            },
            null);
        Assert.False((bool)invalidHavingCriteria.ReturnValue!);

        var enumerableOrders = Expression.Constant(ExampleData.Customers[0].Orders);

        Assert.True(Assert.IsType<bool>(ReflectionTestSupport.InvokeInstance(
            Engine,
            "TryApplyEnumerableObjectFilter",
            typeof(Order),
            enumerableOrders,
            null,
            null)));

        Assert.True(Assert.IsType<bool>(ReflectionTestSupport.InvokeInstance(
            Engine,
            "TryApplyEnumerableObjectFilter",
            typeof(Order),
            enumerableOrders,
            new Dictionary<string, object?>(),
            null)));

        var noopEnumerableFilter = InvokeInstanceWithArguments(
            Engine,
            "TryApplyEnumerableObjectFilter",
            typeof(Customer),
            Expression.Constant(ExampleData.Customers),
            new Dictionary<string, object?>
            {
                ["ordersAggregate"] = new Dictionary<string, object?>()
            },
            null);
        Assert.True((bool)noopEnumerableFilter.ReturnValue!);
        Assert.Same(noopEnumerableFilter.Arguments[1], noopEnumerableFilter.Arguments[3]);

        var invalidEnumerableFilter = InvokeInstanceWithArguments(
            Engine,
            "TryApplyEnumerableObjectFilter",
            typeof(Order),
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["total"] = new Dictionary<string, object?> { ["gt"] = "bad" }
            },
            null);
        Assert.False((bool)invalidEnumerableFilter.ReturnValue!);

        var successfulEnumerableFilter = InvokeInstanceWithArguments(
            Engine,
            "TryApplyEnumerableObjectFilter",
            typeof(Order),
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["status"] = new Dictionary<string, object?> { ["eq"] = "Active" }
            },
            null);
        Assert.True((bool)successfulEnumerableFilter.ReturnValue!);
        Assert.IsAssignableFrom<MethodCallExpression>(successfulEnumerableFilter.Arguments[3]);

        var emptyHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>(),
            null);
        Assert.True((bool)emptyHaving.ReturnValue!);
        Assert.IsType<ConstantExpression>(emptyHaving.Arguments[3]);

        var skippedEmptyHavingValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["count"] = null
            },
            null);
        Assert.True((bool)skippedEmptyHavingValue.ReturnValue!);
        Assert.IsType<ConstantExpression>(skippedEmptyHavingValue.Arguments[3]);

        var countHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["count"] = new Dictionary<string, object?> { ["gt"] = 1 }
            },
            null);
        Assert.True((bool)countHaving.ReturnValue!);
        Assert.IsAssignableFrom<Expression>(countHaving.Arguments[3]);

        var invalidCountHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["count"] = new Dictionary<string, object?> { ["gt"] = "bad" }
            },
            null);
        Assert.False((bool)invalidCountHaving.ReturnValue!);

        var invalidAndHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["and"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["count"] = new Dictionary<string, object?> { ["gt"] = "bad" }
                    }
                }
            },
            null);
        Assert.False((bool)invalidAndHaving.ReturnValue!);

        var invalidOrHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["or"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["count"] = new Dictionary<string, object?> { ["gt"] = "bad" }
                    }
                }
            },
            null);
        Assert.False((bool)invalidOrHaving.ReturnValue!);

        var invalidNotHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["not"] = new Dictionary<string, object?>
                {
                    ["count"] = new Dictionary<string, object?> { ["gt"] = "bad" }
                }
            },
            null);
        Assert.False((bool)invalidNotHaving.ReturnValue!);

        var aggregateHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["sum"] = new Dictionary<string, object?>
                {
                    ["total"] = new Dictionary<string, object?> { ["gt"] = 1_000d }
                }
            },
            null);
        Assert.True((bool)aggregateHaving.ReturnValue!);
        Assert.IsAssignableFrom<Expression>(aggregateHaving.Arguments[3]);

        var aggregateHavingWithSkippedOperations = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["sum"] = new Dictionary<string, object?>
                {
                    ["total"] = null
                }
            },
            null);
        Assert.True((bool)aggregateHavingWithSkippedOperations.ReturnValue!);
        Assert.IsType<ConstantExpression>(aggregateHavingWithSkippedOperations.Arguments[3]);

        var aggregateHavingWithMixedOperations = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["sum"] = new Dictionary<string, object?>
                {
                    ["total"] = null,
                    ["id"] = new Dictionary<string, object?> { ["gt"] = 1d }
                }
            },
            null);
        Assert.True((bool)aggregateHavingWithMixedOperations.ReturnValue!);
        Assert.IsAssignableFrom<Expression>(aggregateHavingWithMixedOperations.Arguments[3]);

        var invalidAggregateFieldHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["sum"] = new Dictionary<string, object?>
                {
                    ["missing"] = new Dictionary<string, object?> { ["gt"] = 1 }
                }
            },
            null);
        Assert.False((bool)invalidAggregateFieldHaving.ReturnValue!);

        var invalidAggregateOperationHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["sum"] = new Dictionary<string, object?>
                {
                    ["total"] = new Dictionary<string, object?> { ["gt"] = "bad" }
                }
            },
            null);
        Assert.False((bool)invalidAggregateOperationHaving.ReturnValue!);

        var unsupportedAggregateHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["stdev"] = new Dictionary<string, object?>
                {
                    ["total"] = new Dictionary<string, object?> { ["gt"] = 1 }
                }
            },
            null);
        Assert.False((bool)unsupportedAggregateHaving.ReturnValue!);

        var andHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["and"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["count"] = new Dictionary<string, object?> { ["gt"] = 1 }
                    }
                }
            },
            null);
        Assert.True((bool)andHaving.ReturnValue!);
        Assert.IsAssignableFrom<Expression>(andHaving.Arguments[3]);

        var orHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["or"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["count"] = new Dictionary<string, object?> { ["gt"] = 100 }
                    },
                    new Dictionary<string, object?>
                    {
                        ["count"] = new Dictionary<string, object?> { ["lt"] = 10 }
                    }
                }
            },
            null);
        Assert.True((bool)orHaving.ReturnValue!);
        Assert.IsAssignableFrom<Expression>(orHaving.Arguments[3]);

        var notHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["not"] = new Dictionary<string, object?>
                {
                    ["count"] = new Dictionary<string, object?> { ["lt"] = 1 }
                }
            },
            null);
        Assert.True((bool)notHaving.ReturnValue!);
        Assert.IsAssignableFrom<Expression>(notHaving.Arguments[3]);

        var emptyNotHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildHavingExpressionForSequence",
            ordersField,
            enumerableOrders,
            new Dictionary<string, object?>
            {
                ["not"] = new Dictionary<string, object?>
                {
                    ["count"] = null
                }
            },
            null);
        Assert.True((bool)emptyNotHaving.ReturnValue!);
        Assert.IsType<ConstantExpression>(emptyNotHaving.Arguments[3]);

        var emptyOrHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildLogicalHavingExpression",
            ordersField,
            enumerableOrders,
            Array.Empty<object>(),
            true,
            null);
        Assert.True((bool)emptyOrHaving.ReturnValue!);
        Assert.IsType<ConstantExpression>(emptyOrHaving.Arguments[4]);

        var orHavingWithEmptyChild = InvokeInstanceWithArguments(
            Engine,
            "TryBuildLogicalHavingExpression",
            ordersField,
            enumerableOrders,
            new object?[] { new Dictionary<string, object?>() },
            true,
            null);
        Assert.True((bool)orHavingWithEmptyChild.ReturnValue!);
        Assert.IsType<ConstantExpression>(orHavingWithEmptyChild.Arguments[4]);

        var andHavingWithEmptyChild = InvokeInstanceWithArguments(
            Engine,
            "TryBuildLogicalHavingExpression",
            ordersField,
            enumerableOrders,
            new object?[] { new Dictionary<string, object?>() },
            false,
            null);
        Assert.True((bool)andHavingWithEmptyChild.ReturnValue!);
        Assert.IsType<ConstantExpression>(andHavingWithEmptyChild.Arguments[4]);

        var invalidLogicalHaving = InvokeInstanceWithArguments(
            Engine,
            "TryBuildLogicalHavingExpression",
            ordersField,
            enumerableOrders,
            new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["count"] = new Dictionary<string, object?> { ["gt"] = "bad" }
                }
            },
            false,
            null);
        Assert.False((bool)invalidLogicalHaving.ReturnValue!);

        var missingAggregateField = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            ordersField,
            enumerableOrders,
            "missing",
            AggregateOperator.Sum,
            null);
        Assert.False((bool)missingAggregateField.ReturnValue!);

        var nullableCountDistinct = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            couponsField,
            Expression.Constant(ExampleData.Securities[2].Details.Coupons),
            "interestRate",
            AggregateOperator.CountDistinct,
            null);
        Assert.True((bool)nullableCountDistinct.ReturnValue!);
        Assert.IsAssignableFrom<MethodCallExpression>(nullableCountDistinct.Arguments[4]);

        var noModelAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            stringRowsField,
            Expression.Constant(new[] { "A", "B" }),
            "value",
            AggregateOperator.Sum,
            null);
        Assert.False((bool)noModelAggregateValue.ReturnValue!);

        var sumAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            ordersField,
            enumerableOrders,
            "total",
            AggregateOperator.Sum,
            null);
        Assert.True((bool)sumAggregateValue.ReturnValue!);

        var avgAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            ordersField,
            enumerableOrders,
            "total",
            AggregateOperator.Avg,
            null);
        Assert.True((bool)avgAggregateValue.ReturnValue!);

        var varAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            ordersField,
            enumerableOrders,
            "total",
            AggregateOperator.Var,
            null);
        Assert.True((bool)varAggregateValue.ReturnValue!);

        var varpAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            ordersField,
            enumerableOrders,
            "total",
            AggregateOperator.Varp,
            null);
        Assert.True((bool)varpAggregateValue.ReturnValue!);

        var nullableVarpAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            couponsField,
            Expression.Constant(ExampleData.Securities[2].Details.Coupons),
            "interestRate",
            AggregateOperator.Varp,
            null);
        Assert.True((bool)nullableVarpAggregateValue.ReturnValue!);

        var nonNumericAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            ordersField,
            enumerableOrders,
            "reference",
            AggregateOperator.Sum,
            null);
        Assert.False((bool)nonNumericAggregateValue.ReturnValue!);

        var nonNumericVarianceAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            ordersField,
            enumerableOrders,
            "reference",
            AggregateOperator.Var,
            null);
        Assert.False((bool)nonNumericVarianceAggregateValue.ReturnValue!);

        var minAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            couponsField,
            Expression.Constant(ExampleData.Securities[2].Details.Coupons),
            "interestRate",
            AggregateOperator.Min,
            null);
        Assert.True((bool)minAggregateValue.ReturnValue!);

        var maxAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            couponsField,
            Expression.Constant(ExampleData.Securities[2].Details.Coupons),
            "interestRate",
            AggregateOperator.Max,
            null);
        Assert.True((bool)maxAggregateValue.ReturnValue!);

        var boolMinAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            GetCollectionField<SecurityDetails>("calls"),
            Expression.Constant(ExampleData.Securities[2].Details.Calls),
            "isCalled",
            AggregateOperator.Min,
            null);
        Assert.True((bool)boolMinAggregateValue.ReturnValue!);

        var unsupportedAggregateValue = InvokeInstanceWithArguments(
            Engine,
            "TryBuildQueryableAggregateValueExpression",
            ordersField,
            enumerableOrders,
            "total",
            AggregateOperator.Stdev,
            null);
        Assert.False((bool)unsupportedAggregateValue.ReturnValue!);

        foreach (var supportedOperator in new[] { "countDistinct", "sum", "avg", "var", "varp", "min", "max" })
        {
            var parsedOperator = InvokeStaticWithArguments(
                typeof(CollectionExecutionEngine),
                "TryParseSupportedQueryableAggregateOperator",
                supportedOperator,
                null);
            Assert.True((bool)parsedOperator.ReturnValue!);
            Assert.IsType<AggregateOperator>(parsedOperator.Arguments[1]);
        }

        var unsupportedOperator = InvokeStaticWithArguments(
            typeof(CollectionExecutionEngine),
            "TryParseSupportedQueryableAggregateOperator",
            "stdev",
            null);
        Assert.False((bool)unsupportedOperator.ReturnValue!);
    }

    private static InvocationResult InvokeInstanceWithArguments(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
        var args = arguments.ToArray();
        var returnValue = method.Invoke(instance, args);
        return new InvocationResult(returnValue, args);
    }

    private static InvocationResult InvokeStaticWithArguments(Type declaringType, string methodName, params object?[] arguments)
    {
        var method = declaringType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
        var args = arguments.ToArray();
        var returnValue = method.Invoke(null, args);
        return new InvocationResult(returnValue, args);
    }

    private static CollectionFieldModel GetCollectionField<THost>(string fieldName)
    {
        var model = Catalog.TryGetObjectType(typeof(THost));
        Assert.NotNull(model);
        var field = model.FindCollection(fieldName);
        Assert.NotNull(field);
        return field;
    }

    private sealed record InvocationResult(object? ReturnValue, object?[] Arguments);

    private sealed class StringRowsHost
    {
        public string[] Rows { get; init; } = [];
    }

    private sealed class NullableOrdersHost
    {
        public Order[]? Orders { get; init; }

        public Order[] RequiredOrders { get; init; } = [];

        public OrderStructEnumerable RequiredOrderStruct { get; init; } = new([]);
    }

    private readonly record struct OrderStructEnumerable(Order[] Orders) : IEnumerable<Order>
    {
        public IEnumerator<Order> GetEnumerator() => ((IEnumerable<Order>)Orders).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class InterceptingQueryable<T>(IQueryable<T> inner, params IReadOnlyList<string> throwOnMethods) : IOrderedQueryable<T>
    {
        private readonly IQueryable<T> _inner = inner;
        private readonly InterceptingQueryProvider _provider = new(inner.Provider, throwOnMethods);

        public Type ElementType => typeof(T);

        public Expression Expression => _inner.Expression;

        public IQueryProvider Provider => _provider;

        public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class InterceptingQueryProvider(IQueryProvider inner, IReadOnlyList<string> throwOnMethods) : IQueryProvider
    {
        private readonly HashSet<string> _throwOnMethods = new(throwOnMethods, StringComparer.Ordinal);

        public IQueryable CreateQuery(Expression expression)
        {
            ThrowIfIntercepted(expression);

            var elementType = expression.Type.GetInterfaces()
                .Concat([expression.Type])
                .First(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQueryable<>))
                .GetGenericArguments()[0];

            var query = inner.CreateQuery(expression);
            var wrapperType = typeof(InterceptingQueryable<>).MakeGenericType(elementType);
            return (IQueryable)Activator.CreateInstance(wrapperType, query, _throwOnMethods.ToArray())!;
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            ThrowIfIntercepted(expression);
            return new InterceptingQueryable<TElement>(inner.CreateQuery<TElement>(expression), _throwOnMethods.ToArray());
        }

        public object? Execute(Expression expression)
        {
            ThrowIfIntercepted(expression);
            return inner.Execute(expression);
        }

        public TResult Execute<TResult>(Expression expression)
        {
            ThrowIfIntercepted(expression);
            return inner.Execute<TResult>(expression);
        }

        private void ThrowIfIntercepted(Expression expression)
        {
            if (expression is MethodCallExpression methodCall &&
                _throwOnMethods.Contains(methodCall.Method.Name))
            {
                throw new InvalidOperationException($"Intercepted {methodCall.Method.Name}.");
            }
        }
    }
}
