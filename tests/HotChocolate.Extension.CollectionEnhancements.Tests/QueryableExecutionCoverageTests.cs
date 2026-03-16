#pragma warning disable CS8605
using System.Linq.Expressions;
using System.Reflection;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Extension.CollectionEnhancements.Execution;
using HotChocolate.Extension.CollectionEnhancements.Metadata;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestData;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

public sealed class QueryableExecutionCoverageTests
{
    private static readonly CollectionSchemaCatalog Catalog = CollectionSchemaCatalog.CreateDefault();
    private static readonly CollectionExecutionEngine Engine = new(Catalog);

    [Fact]
    public void ApplyCollectionArgumentsForField_ShouldPreserveQueryable_WhenFilterSortAndWindowAreTranslatable()
    {
        var securitiesField = GetCollectionField<Query>("securities");

        var result = Engine.ApplyCollectionArgumentsForField(
            securitiesField,
            ExampleData.Securities.AsQueryable(),
            new Dictionary<string, object?>
            {
                ["details"] = new Dictionary<string, object?>
                {
                    ["couponCount"] = new Dictionary<string, object?> { ["gt"] = 2 }
                }
            },
            new object?[]
            {
                new Dictionary<string, object?> { ["price"] = "DESC" }
            },
            offset: 1,
            limit: 1);

        Assert.IsAssignableFrom<IExecutable>(result);
        var queryable = AsQueryable<Security>(result);
        var expressionText = queryable.Expression.ToString();

        Assert.Contains("Where", expressionText, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending", expressionText, StringComparison.Ordinal);
        Assert.Contains("Skip", expressionText, StringComparison.Ordinal);
        Assert.Contains("Take", expressionText, StringComparison.Ordinal);

        var row = Assert.Single(queryable.Cast<Security>());
        Assert.Equal(1, row.Id);

        var fallbackResult = Engine.ApplyCollectionArgumentsForField(
            GetCollectionField<Query>("customers"),
            ExampleData.Customers.AsQueryable(),
            new Dictionary<string, object?>
            {
                ["ordersAggregate"] = new Dictionary<string, object?>
                {
                    ["having"] = new Dictionary<string, object?>
                    {
                        ["count"] = new Dictionary<string, object?> { ["gte"] = 2 }
                    }
                }
            },
            order: null,
            offset: null,
            limit: null);

        Assert.IsAssignableFrom<IExecutable>(fallbackResult);
        var aggregateFilteredQueryable = AsQueryable<Customer>(fallbackResult);
        Assert.Contains("Count", aggregateFilteredQueryable.Expression.ToString(), StringComparison.Ordinal);

        var enumerableExecutable = Engine.ApplyCollectionArgumentsForField(
            securitiesField,
            ExampleData.Securities,
            where: null,
            order: null,
            offset: null,
            limit: 2);
        Assert.IsAssignableFrom<IExecutable>(enumerableExecutable);
        Assert.Equal([1, 2], AsQueryable<Security>(enumerableExecutable).Select(security => security.Id).ToArray());

        var nonQueryableApplied = Engine.TryApplyQueryableCollectionArguments(
            securitiesField,
            ExampleData.Securities,
            where: null,
            order: null,
            offset: null,
            limit: null,
            out var nonQueryableResult);
        Assert.False(nonQueryableApplied);
        Assert.Null(nonQueryableResult);
    }

    [Fact]
    public void QueryableObjectFilterHelpers_ShouldCoverLogicalNestedAndFallbackBranches()
    {
        var parameter = Expression.Parameter(typeof(Security), "row");

        var complexFilterCall = InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Security),
            parameter,
            new Dictionary<string, object?>
            {
                ["and"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["currency"] = new Dictionary<string, object?> { ["eq"] = "USD" }
                    },
                    new Dictionary<string, object?>
                    {
                        ["details"] = new Dictionary<string, object?>
                        {
                            ["couponCount"] = new Dictionary<string, object?> { ["gt"] = 5 }
                        }
                    }
                },
                ["or"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["price"] = new Dictionary<string, object?> { ["gte"] = 100 }
                    }
                },
                ["not"] = new Dictionary<string, object?>
                {
                    ["isin"] = new Dictionary<string, object?> { ["eq"] = "blocked" }
                }
            },
            null);
        Assert.True((bool)complexFilterCall.ReturnValue!);
        Assert.IsAssignableFrom<Expression>(complexFilterCall.Arguments[3]);

        var simpleFilterCall = InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Security),
            parameter,
            new Dictionary<string, object?>
            {
                ["currency"] = new Dictionary<string, object?> { ["eq"] = "USD" }
            },
            null);
        var predicate = Assert.IsAssignableFrom<Expression>(simpleFilterCall.Arguments[3]);

        Assert.True(EvaluatePredicate(predicate, parameter, ExampleData.Securities[0]));
        Assert.False(EvaluatePredicate(predicate, parameter, ExampleData.Securities[1]));

        Assert.True((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Customer),
            Expression.Parameter(typeof(Customer), "customer"),
            new Dictionary<string, object?>
            {
                ["ordersAggregate"] = new Dictionary<string, object?>()
            },
            null).ReturnValue!);

        Assert.True((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Customer),
            Expression.Parameter(typeof(Customer), "customer"),
            new Dictionary<string, object?>
            {
                ["ordersAggregate"] = 1
            },
            null).ReturnValue!);

        Assert.True((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Customer),
            Expression.Parameter(typeof(Customer), "customer"),
            new Dictionary<string, object?>
            {
                ["ordersGroup"] = new Dictionary<string, object?>()
            },
            null).ReturnValue!);

        var aggregateCriteriaExpression = InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Customer),
            Expression.Parameter(typeof(Customer), "customer"),
            new Dictionary<string, object?>
            {
                ["ordersAggregate"] = new Dictionary<string, object?>
                {
                    ["having"] = new Dictionary<string, object?>
                    {
                        ["count"] = new Dictionary<string, object?> { ["gt"] = 1 }
                    }
                }
            },
            null);
        Assert.True((bool)aggregateCriteriaExpression.ReturnValue!);
        Assert.IsAssignableFrom<Expression>(aggregateCriteriaExpression.Arguments[3]);

        Assert.True((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Customer),
            Expression.Parameter(typeof(Customer), "customer"),
            new Dictionary<string, object?>
            {
                ["ordersGroup"] = new Dictionary<string, object?>
                {
                    ["by"] = new object?[] { "status" }
                }
            },
            null).ReturnValue!);

        Assert.False((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Security),
            parameter,
            new Dictionary<string, object?>
            {
                ["missing"] = new Dictionary<string, object?> { ["eq"] = 1 }
            },
            null).ReturnValue!);

        Assert.False((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(string),
            Expression.Parameter(typeof(string), "text"),
            new Dictionary<string, object?>(),
            null).ReturnValue!);

        var emptyAnd = Assert.IsAssignableFrom<Expression>(InvokeStaticWithArguments(
            typeof(CollectionExecutionEngine),
            "CombineAnd",
            (object)Array.Empty<Expression>()).ReturnValue ?? Expression.Constant(true));
        Assert.Equal("True", emptyAnd.ToString());

        Assert.NotNull(InvokeStaticWithArguments(
            typeof(CollectionExecutionEngine),
            "CombineOr",
            (object)new Expression[] { Expression.Constant(true), Expression.Constant(false) }).ReturnValue);

        Assert.IsAssignableFrom<Expression>(InvokeStaticWithArguments(
            typeof(CollectionExecutionEngine),
            "GuardNull",
            Expression.Parameter(typeof(int), "value"),
            Expression.Constant(true)).ReturnValue);

        Assert.True((bool)ReflectionTestSupport.InvokeInstance(
            Engine,
            "TryApplyQueryableObjectFilter",
            typeof(Security),
            ExampleData.Securities.AsQueryable(),
            null,
            null));

        Assert.True((bool)ReflectionTestSupport.InvokeInstance(
            Engine,
            "TryApplyQueryableObjectFilter",
            typeof(Security),
            ExampleData.Securities.AsQueryable(),
            new Dictionary<string, object?>(),
            null));

        var noopQueryableFilter = InvokeInstanceWithArguments(
            Engine,
            "TryApplyQueryableObjectFilter",
            typeof(Customer),
            ExampleData.Customers.AsQueryable(),
            new Dictionary<string, object?>
            {
                ["ordersAggregate"] = new Dictionary<string, object?>()
            },
            null);
        Assert.True((bool)noopQueryableFilter.ReturnValue!);

        var filterApplied = (bool)ReflectionTestSupport.InvokeInstance(
            Engine,
            "TryApplyQueryableObjectFilter",
            typeof(Security),
            ExampleData.Securities.AsQueryable(),
            new Dictionary<string, object?>
            {
                ["currency"] = new Dictionary<string, object?> { ["eq"] = "JPY" }
            },
            null);
        Assert.True(filterApplied);

        var nonModelFilterApplied = (bool)ReflectionTestSupport.InvokeInstance(
            Engine,
            "TryApplyQueryableObjectFilter",
            typeof(string),
            new[] { "a" }.AsQueryable(),
            new Dictionary<string, object?>
            {
                ["length"] = new Dictionary<string, object?> { ["eq"] = 1 }
            },
            null);
        Assert.False(nonModelFilterApplied);

        Assert.False((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Security),
            parameter,
            new Dictionary<string, object?>
            {
                ["id"] = new Dictionary<string, object?> { ["gt"] = "bad" }
            },
            null).ReturnValue!);

        Assert.False((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Security),
            parameter,
            new Dictionary<string, object?>
            {
                ["details"] = new Dictionary<string, object?>
                {
                    ["couponCount"] = new Dictionary<string, object?> { ["gt"] = "bad" }
                }
            },
            null).ReturnValue!);

        Assert.False((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Security),
            parameter,
            new Dictionary<string, object?>
            {
                ["and"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = new Dictionary<string, object?> { ["gt"] = "bad" }
                    }
                }
            },
            null).ReturnValue!);

        Assert.False((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Security),
            parameter,
            new Dictionary<string, object?>
            {
                ["or"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = new Dictionary<string, object?> { ["gt"] = "bad" }
                    }
                }
            },
            null).ReturnValue!);

        Assert.False((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Security),
            parameter,
            new Dictionary<string, object?>
            {
                ["not"] = new Dictionary<string, object?>
                {
                    ["id"] = new Dictionary<string, object?> { ["gt"] = "bad" }
                }
            },
            null).ReturnValue!);

        var emptyNot = InvokeInstanceWithArguments(
            Engine,
            "TryBuildObjectFilterExpression",
            typeof(Customer),
            Expression.Parameter(typeof(Customer), "customer"),
            new Dictionary<string, object?>
            {
                ["not"] = new Dictionary<string, object?>
                {
                    ["ordersAggregate"] = 1
                }
            },
            null);
        Assert.True((bool)emptyNot.ReturnValue!);
        Assert.IsType<ConstantExpression>(emptyNot.Arguments[3]);

        var emptyOrList = InvokeInstanceWithArguments(
            Engine,
            "TryBuildLogicalListExpression",
            typeof(Security),
            parameter,
            Array.Empty<object>(),
            true,
            null);
        Assert.True((bool)emptyOrList.ReturnValue!);
        Assert.IsType<ConstantExpression>(emptyOrList.Arguments[4]);

        var orWithEmptyChild = InvokeInstanceWithArguments(
            Engine,
            "TryBuildLogicalListExpression",
            typeof(Security),
            parameter,
            new object?[] { new Dictionary<string, object?>() },
            true,
            null);
        Assert.True((bool)orWithEmptyChild.ReturnValue!);
        Assert.True(orWithEmptyChild.Arguments[4] is null or ConstantExpression);

        var andWithEmptyChild = InvokeInstanceWithArguments(
            Engine,
            "TryBuildLogicalListExpression",
            typeof(Security),
            parameter,
            new object?[] { new Dictionary<string, object?>() },
            false,
            null);
        Assert.True((bool)andWithEmptyChild.ReturnValue!);
        Assert.Null(andWithEmptyChild.Arguments[4]);

        Assert.False((bool)InvokeInstanceWithArguments(
            Engine,
            "TryBuildLogicalListExpression",
            typeof(Security),
            parameter,
            new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = new Dictionary<string, object?> { ["gt"] = "bad" }
                }
            },
            false,
            null).ReturnValue!);
    }

    [Fact]
    public void QueryableScalarAndSortHelpers_ShouldCoverConversionsComparisonsAndFallbacks()
    {
        var intParameter = Expression.Parameter(typeof(int?), "value");
        var stringParameter = Expression.Parameter(typeof(string), "text");
        var boolParameter = Expression.Parameter(typeof(bool), "flag");

        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildScalarOperationsExpression",
            intParameter,
            new Dictionary<string, object?>
            {
                ["eq"] = 5,
                ["neq"] = 7,
                ["in"] = new object?[] { 4, 5 },
                ["nin"] = new object?[] { 8, 9 },
                ["gt"] = 4,
                ["gte"] = 5,
                ["lt"] = 6,
                ["lte"] = 5,
                ["unsupported"] = 1
            },
            null));

        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildScalarOperationsExpression",
            stringParameter,
            new Dictionary<string, object?>
            {
                ["gt"] = "AAA",
                ["lte"] = "ZZZ"
            },
            null));

        Assert.False((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildScalarOperationsExpression",
            boolParameter,
            new Dictionary<string, object?>
            {
                ["gt"] = true
            },
            null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildScalarOperationsExpression",
            intParameter,
            new Dictionary<string, object?>
            {
                ["eq"] = null
            },
            null));

        Assert.False((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildScalarOperationExpression",
            Expression.Parameter(typeof(int), "plain"),
            "eq",
            "bad",
            null));
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildScalarOperationExpression",
            Expression.Parameter(typeof(int), "plain"),
            "in",
            new object?[] { "bad" },
            null));

        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildSetExpression",
            intParameter,
            new object?[] { 1, 2, null },
            null));
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildSetExpression",
            Expression.Parameter(typeof(int), "plain"),
            new object?[] { "bad" },
            null));

        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildOrderedComparisonExpression",
            stringParameter,
            "gt",
            "AAA",
            null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildOrderedComparisonExpression",
            intParameter,
            "lt",
            5,
            null));
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildOrderedComparisonExpression",
            boolParameter,
            "gte",
            true,
            null));
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildOrderedComparisonExpression",
            intParameter,
            "gt",
            "bad",
            null));
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildOrderedComparisonExpression",
            intParameter,
            "between",
            5,
            null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildOrderedComparisonExpression",
            Expression.Parameter(typeof(int), "plain"),
            "gte",
            5,
            null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryBuildOrderedComparisonExpression",
            Expression.Parameter(typeof(int), "plain"),
            "lte",
            5,
            null));

        foreach (var operation in new[] { "gt", "gte", "lt", "lte" })
        {
            Assert.NotNull(ReflectionTestSupport.InvokeStatic(
                typeof(CollectionExecutionEngine),
                "BuildNumericComparison",
                Expression.Constant(1),
                operation));
        }
        Assert.Throws<TargetInvocationException>(() =>
            ReflectionTestSupport.InvokeStatic(
                typeof(CollectionExecutionEngine),
                "BuildNumericComparison",
                Expression.Constant(1),
                "nope"));

        Assert.NotNull(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "BuildNullableComparison",
            intParameter,
            Expression.GreaterThan(intParameter, Expression.Convert(Expression.Constant(0), typeof(int?))),
            "gt"));
        Assert.NotNull(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "BuildNullableComparison",
            stringParameter,
            Expression.Constant(true),
            "lt"));

        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryCreateTypedConstant",
            typeof(int?),
            5,
            null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryCreateTypedConstant",
            typeof(string),
            null,
            null));
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "TryCreateTypedConstant",
            typeof(int),
            null,
            null));

        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", null, typeof(int?), null));
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", null, typeof(int), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", "Completed", typeof(OrderStatus), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", OrderStatus.Active, typeof(OrderStatus), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", 1, typeof(OrderStatus), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", Guid.NewGuid().ToString(), typeof(Guid), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", Guid.NewGuid(), typeof(Guid), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", new DateOnly(2026, 3, 15), typeof(DateOnly), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", new DateTime(2026, 3, 15), typeof(DateOnly), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", "2026-03-15", typeof(DateOnly), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", new DateTime(2026, 3, 15), typeof(DateTime), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", new DateOnly(2026, 3, 15), typeof(DateTime), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", "2026-03-15T00:00:00Z", typeof(DateTime), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", 5, typeof(string), null));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", "5", typeof(int), null));
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "TryConvertValue", "nope", typeof(Guid), null));

        var securityParameter = Expression.Parameter(typeof(Security), "security");
        Assert.NotNull(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "BuildMemberAccess",
            securityParameter,
            typeof(Security).GetProperty(nameof(Security.Id))!));
        Assert.NotNull(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "BuildMemberAccess",
            Expression.Parameter(typeof(DummyHost), "host"),
            typeof(DummyHost).GetField(nameof(DummyHost.Field))!));
        var unsupportedMemberFailure = Assert.Throws<TargetInvocationException>(() =>
            ReflectionTestSupport.InvokeStatic(
                typeof(CollectionExecutionEngine),
                "BuildMemberAccess",
                Expression.Parameter(typeof(DummyHost), "host"),
                typeof(DummyHost).GetMethod(nameof(DummyHost.Method))!));
        Assert.Contains("Unsupported member", unsupportedMemberFailure.InnerException!.Message, StringComparison.Ordinal);

        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "CanUseOrderedComparison", typeof(int?)));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "CanUseOrderedComparison", typeof(string)));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "CanUseOrderedComparison", typeof(DateOnly)));
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "CanUseOrderedComparison", typeof(bool)));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "CanBeNull", typeof(string)));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "CanBeNull", typeof(int?)));
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "CanBeNull", typeof(int)));

        var sortApplied = (bool)ReflectionTestSupport.InvokeInstance(
            Engine,
            "TryApplyQueryableSort",
            typeof(Security),
            ExampleData.Securities.AsQueryable(),
            new object?[] { new Dictionary<string, object?> { ["price"] = "ASC" } },
            null);
        Assert.True(sortApplied);

        var noSortApplied = (bool)ReflectionTestSupport.InvokeInstance(
            Engine,
            "TryApplyQueryableSort",
            typeof(Security),
            ExampleData.Securities.AsQueryable(),
            null,
            null);
        Assert.True(noSortApplied);

        var multiClauseSort = InvokeInstanceWithArguments(
            Engine,
            "TryApplyQueryableSort",
            typeof(Security),
            ExampleData.Securities.AsQueryable(),
            new object?[]
            {
                new Dictionary<string, object?> { ["currency"] = "ASC" },
                new Dictionary<string, object?> { ["price"] = "DESC" }
            },
            null);
        Assert.True((bool)multiClauseSort.ReturnValue!);

        var thenByAscendingSort = InvokeInstanceWithArguments(
            Engine,
            "TryApplyQueryableSort",
            typeof(Security),
            ExampleData.Securities.AsQueryable(),
            new object?[]
            {
                new Dictionary<string, object?> { ["currency"] = "DESC" },
                new Dictionary<string, object?> { ["id"] = "ASC" }
            },
            null);
        Assert.True((bool)thenByAscendingSort.ReturnValue!);

        var unsupportedSort = (bool)ReflectionTestSupport.InvokeInstance(
            Engine,
            "TryApplyQueryableSort",
            typeof(Security),
            ExampleData.Securities.AsQueryable(),
            new object?[] { new Dictionary<string, object?> { ["missing"] = "ASC" } },
            null);
        Assert.False(unsupportedSort);

        var noModelSort = (bool)ReflectionTestSupport.InvokeInstance(
            Engine,
            "TryApplyQueryableSort",
            typeof(string),
            new[] { "a" }.AsQueryable(),
            new object?[] { new Dictionary<string, object?> { ["length"] = "ASC" } },
            null);
        Assert.False(noModelSort);

        var tryApplyUnsupportedSort = Engine.TryApplyQueryableCollectionArguments(
            GetCollectionField<Query>("securities"),
            ExampleData.Securities.AsQueryable(),
            where: null,
            order: new object?[] { new Dictionary<string, object?> { ["missing"] = "ASC" } },
            offset: null,
            limit: null,
            out _);
        Assert.False(tryApplyUnsupportedSort);

        var windowed = Assert.IsAssignableFrom<IQueryable>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ApplyQueryableWindow",
            typeof(Security),
            ExampleData.Securities.AsQueryable(),
            1,
            2));
        Assert.Equal([2, 3], windowed.Cast<Security>().Select(security => security.Id).ToArray());

        var mismatchedQueryableSequence = ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ConvertToTypedSequence",
            typeof(Customer),
            Array.Empty<Security>().AsQueryable());
        var mismatchedTypedArray = Assert.IsType<Customer[]>(mismatchedQueryableSequence);
        Assert.Empty(mismatchedTypedArray);

        var securityArray = ExampleData.Securities.ToArray();
        var arraySequence = ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ConvertToTypedSequence",
            typeof(Security),
            securityArray);
        Assert.Same(securityArray, arraySequence);

        var singleValueSequence = ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ConvertToTypedSequence",
            typeof(Security),
            ExampleData.Securities[0]);
        Assert.Equal([ExampleData.Securities[0].Id], ((Security[])singleValueSequence!).Select(security => security.Id).ToArray());

        var singleExecutable = ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "WrapAsExecutable",
            typeof(Security),
            ExampleData.Securities[0]);
        Assert.IsAssignableFrom<IExecutable>(singleExecutable);

        var groupedExecutable = Engine.CreateGroupRowsForField(
            GetCollectionField<Query>("customers"),
            ExampleData.Customers,
            new object?[] { "name" },
            where: null,
            having: null,
            order: null,
            offset: null,
            limit: null,
            isFlat: false,
            expand: null);
        Assert.IsAssignableFrom<IExecutable>(groupedExecutable);
        Assert.Equal(3, AsQueryable<GroupRowResult>(groupedExecutable).Count());

        var flatExecutable = Engine.ApplyFlatArgumentsForField(
            GetCollectionField<Query>("securities"),
            ExampleData.Securities,
            ["details.coupons"],
            where: null,
            order: null,
            offset: null,
            limit: 2);
        Assert.IsAssignableFrom<IExecutable>(flatExecutable);
        Assert.Equal(2, AsQueryable<IReadOnlyDictionary<string, object?>>(flatExecutable).Count());
    }

    private static bool EvaluatePredicate<T>(Expression expression, ParameterExpression parameter, T value)
    {
        var lambda = Expression.Lambda<Func<T, bool>>(expression, (ParameterExpression)parameter);
        return lambda.Compile()(value);
    }

    private static IQueryable<T> AsQueryable<T>(object executable) =>
        DataEnumerableExtensions.AsQueryable((dynamic)executable);

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
}
#pragma warning restore CS8605
