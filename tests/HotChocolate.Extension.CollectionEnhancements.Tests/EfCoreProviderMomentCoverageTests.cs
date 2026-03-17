using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using HotChocolate.Extension.CollectionEnhancements.Execution;
using HotChocolate.Extension.CollectionEnhancements.Metadata;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestData;
using HotChocolate.Execution.Configuration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

public sealed class EfCoreProviderMomentCoverageTests
{
    private static readonly CollectionSchemaCatalog Catalog = CollectionSchemaCatalog.CreateDefault();
    private static readonly CollectionFieldModel OrdersField = GetCollectionField<Customer>("orders");
    private static readonly CollectionExecutionEngine FastEngine = new(
        Catalog,
        new CollectionEnhancementOptions
        {
            StatisticalMomentsExecutionMode = StatisticalMomentsExecutionMode.Fast
        });
    private static readonly CollectionExecutionEngine StableEngine = new(
        Catalog,
        new CollectionEnhancementOptions
        {
            StatisticalMomentsExecutionMode = StatisticalMomentsExecutionMode.Stable
        });

    [Fact]
    public void AddCollectionEnhancements_ShouldRegisterConfiguredOptions()
    {
        var services = new ServiceCollection();
        services.AddGraphQLServer()
            .AddCollectionEnhancements(options =>
            {
                options.StatisticalMomentsExecutionMode = StatisticalMomentsExecutionMode.Stable;
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<CollectionEnhancementOptions>();

        Assert.Equal(StatisticalMomentsExecutionMode.Stable, options.StatisticalMomentsExecutionMode);
    }

    [Fact]
    public void EfCoreProviderSupport_ShouldDetectKnownProviderFamilies()
    {
        Assert.True(EfCoreProviderSupport.TryDetect(CreateProviderQueryable("Microsoft.EntityFrameworkCore.SqlServer", relational: true), out var sqlServerInfo));
        Assert.Equal(EfCoreProviderFamily.SqlServer, sqlServerInfo.Family);
        Assert.True(sqlServerInfo.IsRelational);

        Assert.True(EfCoreProviderSupport.TryDetect(CreateProviderQueryable("Npgsql.EntityFrameworkCore.PostgreSQL", relational: true), out var postgreSqlInfo));
        Assert.Equal(EfCoreProviderFamily.PostgreSql, postgreSqlInfo.Family);

        Assert.True(EfCoreProviderSupport.TryDetect(CreateProviderQueryable("Oracle.EntityFrameworkCore", relational: true), out var oracleInfo));
        Assert.Equal(EfCoreProviderFamily.Oracle, oracleInfo.Family);

        Assert.True(EfCoreProviderSupport.TryDetect(CreateProviderQueryable("Microsoft.EntityFrameworkCore.Sqlite", relational: true), out var sqliteInfo));
        Assert.Equal(EfCoreProviderFamily.Sqlite, sqliteInfo.Family);

        Assert.True(EfCoreProviderSupport.TryDetect(CreateProviderQueryable("Pomelo.EntityFrameworkCore.MySql", relational: true), out var genericInfo));
        Assert.Equal(EfCoreProviderFamily.RelationalGeneric, genericInfo.Family);

        Assert.True(EfCoreProviderSupport.TryDetect(CreateProviderQueryable("Microsoft.EntityFrameworkCore.InMemory", relational: false), out var nonRelationalInfo));
        Assert.Equal(EfCoreProviderFamily.NonRelational, nonRelationalInfo.Family);
        Assert.False(nonRelationalInfo.IsRelational);

        Assert.True(EfCoreProviderSupport.TryDetect(CreateProviderQueryable(null, relational: true), out var unknownRelationalInfo));
        Assert.Equal(EfCoreProviderFamily.RelationalGeneric, unknownRelationalInfo.Family);

        Assert.False(EfCoreProviderSupport.TryDetect(CreateProviderQueryable(null, relational: false), out _));
        Assert.False(EfCoreProviderSupport.TryDetect(ExampleData.Customers[0].Orders.AsQueryable(), out _));
    }

    [Fact]
    public void OracleProviderPackage_ShouldNotExposeNativeVarianceAndStandardDeviationMethods()
    {
        var assembly = Assembly.Load("Oracle.EntityFrameworkCore");
        var extensionsType = assembly.GetTypes()
            .Single(type => string.Equals(type.Name, "OracleDbFunctionsExtensions", StringComparison.Ordinal));
        var methodNames = extensionsType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain("VarianceSample", methodNames);
        Assert.DoesNotContain("VariancePopulation", methodNames);
        Assert.DoesNotContain("StandardDeviationSample", methodNames);
        Assert.DoesNotContain("StandardDeviationPopulation", methodNames);

        Assert.Null(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "GetProviderAggregateExtensionsType",
            "Definitely.Missing.Provider",
            "MissingDbFunctionsExtensions"));

        var missingMethods = Assert.IsType<MethodInfo[]>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "GetProviderAggregateExtensionMethods",
            "Definitely.Missing.Provider",
            "MissingDbFunctionsExtensions"));
        Assert.Empty(missingMethods);

        var sqlServerMethods = Assert.IsType<MethodInfo[]>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "GetProviderAggregateExtensionMethods",
            "Microsoft.EntityFrameworkCore.SqlServer",
            "SqlServerDbFunctionsExtensions"));
        Assert.Contains(sqlServerMethods, method => string.Equals(method.Name, "VarianceSample", StringComparison.Ordinal));

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetNativeVarianceMethod",
            EfCoreProviderFamily.Oracle,
            typeof(decimal),
            false,
            null,
            null,
            out _)!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetNativeVarianceMethod",
            EfCoreProviderFamily.Oracle,
            typeof(decimal),
            true,
            null,
            null,
            out _)!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetNativeStandardDeviationMethod",
            EfCoreProviderFamily.Oracle,
            typeof(decimal),
            false,
            null,
            null,
            out _)!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetNativeStandardDeviationMethod",
            EfCoreProviderFamily.Oracle,
            typeof(decimal),
            true,
            null,
            null,
            out _)!);
    }

    [Fact]
    public void FastMode_ShouldUseGenericRelationalMomentFallback_ForSqliteAndOracle()
    {
        var sqliteSelection = new AggregateSelectionContext(
            OrdersField,
            isFlat: false,
            CreateProviderQueryable("Microsoft.EntityFrameworkCore.Sqlite", relational: true));
        var oracleSelection = new AggregateSelectionContext(
            OrdersField,
            isFlat: false,
            CreateProviderQueryable("Oracle.EntityFrameworkCore", relational: true));

        foreach (var aggregateOperator in new[]
                 {
                     AggregateOperator.Var,
                     AggregateOperator.Varp,
                     AggregateOperator.Stdev,
                     AggregateOperator.Stdevp,
                     AggregateOperator.Skew,
                     AggregateOperator.Kurtosis
                 })
        {
            var expected = ResolveRowAggregate(aggregateOperator);
            AssertAggregateEquivalent(
                expected,
                FastEngine.ResolveAggregateProjectionField(new AggregateProjection(sqliteSelection, aggregateOperator, null, null), "total"));
            AssertAggregateEquivalent(
                expected,
                FastEngine.ResolveAggregateProjectionField(new AggregateProjection(oracleSelection, aggregateOperator, null, null), "total"));
        }
    }

    [Fact]
    public void FastMode_ShouldUseNativeVarianceAndStandardDeviation_ForSqlServerAndPostgreSql()
    {
        var sqlServerInvocations = new List<string>();
        var sqlServerSelection = new AggregateSelectionContext(
            OrdersField,
            isFlat: false,
            CreateProviderQueryable(
                "Microsoft.EntityFrameworkCore.SqlServer",
                relational: true,
                interceptor: methodCall =>
                {
                    sqlServerInvocations.Add(methodCall.Method.Name);
                    return methodCall.Method.Name switch
                    {
                        "VarianceSample" => 156.25d,
                        "VariancePopulation" => 85.5625d,
                        "StandardDeviationSample" => 12.5d,
                        "StandardDeviationPopulation" => 9.25d,
                        _ => ServiceBackedQueryProvider.UnhandledExecution.Instance
                    };
                }));

        Assert.Equal(
            156.25d,
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(sqlServerSelection, AggregateOperator.Var, null, null),
                "total"));
        Assert.Equal(
            85.5625d,
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(sqlServerSelection, AggregateOperator.Varp, null, null),
                "total"));
        Assert.Equal(
            12.5d,
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(sqlServerSelection, AggregateOperator.Stdev, null, null),
                "total"));
        Assert.Equal(
            9.25d,
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(sqlServerSelection, AggregateOperator.Stdevp, null, null),
                "total"));
        Assert.Contains("VarianceSample", sqlServerInvocations);
        Assert.Contains("VariancePopulation", sqlServerInvocations);
        Assert.Contains("StandardDeviationSample", sqlServerInvocations);
        Assert.Contains("StandardDeviationPopulation", sqlServerInvocations);

        var npgsqlInvocations = new List<string>();
        var npgsqlSelection = new AggregateSelectionContext(
            OrdersField,
            isFlat: false,
            CreateProviderQueryable(
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                relational: true,
                interceptor: methodCall =>
                {
                    npgsqlInvocations.Add(methodCall.Method.Name);
                    return methodCall.Method.Name switch
                    {
                        "VarianceSample" => 76.5625d,
                        "VariancePopulation" => 56.25d,
                        "StandardDeviationSample" => 8.75d,
                        "StandardDeviationPopulation" => 7.5d,
                        _ => ServiceBackedQueryProvider.UnhandledExecution.Instance
                    };
                }));

        Assert.Equal(
            76.5625d,
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(npgsqlSelection, AggregateOperator.Var, null, null),
                "total"));
        Assert.Equal(
            56.25d,
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(npgsqlSelection, AggregateOperator.Varp, null, null),
                "total"));
        Assert.Equal(
            8.75d,
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(npgsqlSelection, AggregateOperator.Stdev, null, null),
                "total"));
        Assert.Equal(
            7.5d,
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(npgsqlSelection, AggregateOperator.Stdevp, null, null),
                "total"));
        Assert.Contains("VarianceSample", npgsqlInvocations);
        Assert.Contains("VariancePopulation", npgsqlInvocations);
        Assert.Contains("StandardDeviationSample", npgsqlInvocations);
        Assert.Contains("StandardDeviationPopulation", npgsqlInvocations);

        var expectedSkew = ResolveRowAggregate(AggregateOperator.Skew);
        AssertAggregateEquivalent(
            expectedSkew,
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(sqlServerSelection, AggregateOperator.Skew, null, null),
                "total"));
    }

    [Fact]
    public void FastMode_ShouldFallbackForOracle_WhenProviderPackageDoesNotExposeNativeMomentAggregates()
    {
        var oracleInvocations = new List<string>();
        var oracleSelection = new AggregateSelectionContext(
            OrdersField,
            isFlat: false,
            CreateProviderQueryable(
                "Oracle.EntityFrameworkCore",
                relational: true,
                interceptor: methodCall =>
                {
                    oracleInvocations.Add(methodCall.Method.Name);
                    return methodCall.Method.Name switch
                    {
                        "VarianceSample" => 91.5d,
                        "VariancePopulation" => 68.625d,
                        "StandardDeviationSample" => 9.565563234854496d,
                        "StandardDeviationPopulation" => 8.284020762890446d,
                        _ => ServiceBackedQueryProvider.UnhandledExecution.Instance
                    };
                }));

        AssertAggregateEquivalent(
            ResolveRowAggregate(AggregateOperator.Var),
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(oracleSelection, AggregateOperator.Var, null, null),
                "total"));
        AssertAggregateEquivalent(
            ResolveRowAggregate(AggregateOperator.Varp),
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(oracleSelection, AggregateOperator.Varp, null, null),
                "total"));
        AssertAggregateEquivalent(
            ResolveRowAggregate(AggregateOperator.Stdev),
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(oracleSelection, AggregateOperator.Stdev, null, null),
                "total"));
        AssertAggregateEquivalent(
            ResolveRowAggregate(AggregateOperator.Stdevp),
            FastEngine.ResolveAggregateProjectionField(
                new AggregateProjection(oracleSelection, AggregateOperator.Stdevp, null, null),
                "total"));

        Assert.DoesNotContain("VarianceSample", oracleInvocations);
        Assert.DoesNotContain("VariancePopulation", oracleInvocations);
        Assert.DoesNotContain("StandardDeviationSample", oracleInvocations);
        Assert.DoesNotContain("StandardDeviationPopulation", oracleInvocations);
    }

    [Fact]
    public void StableMode_ShouldFallbackToWelfordIteration_ForRelationalProviders()
    {
        var stableSelection = new AggregateSelectionContext(
            OrdersField,
            isFlat: false,
            CreateProviderQueryable(
                "Microsoft.EntityFrameworkCore.SqlServer",
                relational: true,
                interceptor: methodCall =>
                    methodCall.Method.Name.StartsWith("StandardDeviation", StringComparison.Ordinal) ||
                    methodCall.Method.Name.StartsWith("Variance", StringComparison.Ordinal)
                        ? throw new InvalidOperationException("Stable mode should not call provider-native variance or stddev.")
                        : ServiceBackedQueryProvider.UnhandledExecution.Instance));

        foreach (var aggregateOperator in new[]
                 {
                     AggregateOperator.Var,
                     AggregateOperator.Varp,
                     AggregateOperator.Stdev,
                     AggregateOperator.Stdevp,
                     AggregateOperator.Skew,
                     AggregateOperator.Kurtosis
                 })
        {
            AssertAggregateEquivalent(
                ResolveRowAggregate(aggregateOperator),
                StableEngine.ResolveAggregateProjectionField(
                    new AggregateProjection(stableSelection, aggregateOperator, null, null),
                    "total"));
        }
    }

    [Fact]
    public void StableMode_ShouldUseProjectedScalarEnumeration_BeforeFullRowFallback()
    {
        var projectedOnlySelection = new AggregateSelectionContext(
            OrdersField,
            isFlat: false,
            CreateProviderQueryable(
                "Microsoft.EntityFrameworkCore.Sqlite",
                relational: true,
                throwOnSourceEnumeration: true));

        AssertAggregateEquivalent(
            ResolveRowAggregate(AggregateOperator.Stdev),
            StableEngine.ResolveAggregateProjectionField(
                new AggregateProjection(projectedOnlySelection, AggregateOperator.Stdev, null, null),
                "total"));

        var finalFallbackSelection = new AggregateSelectionContext(
            OrdersField,
            isFlat: false,
            CreateProviderQueryable(
                "Microsoft.EntityFrameworkCore.Sqlite",
                relational: true,
                throwOnCreateQuery: true));

        AssertAggregateEquivalent(
            ResolveRowAggregate(AggregateOperator.Stdev),
            StableEngine.ResolveAggregateProjectionField(
                new AggregateProjection(finalFallbackSelection, AggregateOperator.Stdev, null, null),
                "total"));
    }

    [Fact]
    public void FastMode_ShouldReturnNullOrZero_ForEdgeCaseMomentInputs()
    {
        var emptySelection = new AggregateSelectionContext(
            OrdersField,
            isFlat: false,
            CreateProviderQueryable("Microsoft.EntityFrameworkCore.Sqlite", relational: true, source: Array.Empty<Order>().AsQueryable()));

        Assert.Null(FastEngine.ResolveAggregateProjectionField(new AggregateProjection(emptySelection, AggregateOperator.Var, null, null), "total"));
        Assert.Null(FastEngine.ResolveAggregateProjectionField(new AggregateProjection(emptySelection, AggregateOperator.Varp, null, null), "total"));
        Assert.Null(FastEngine.ResolveAggregateProjectionField(new AggregateProjection(emptySelection, AggregateOperator.Stdev, null, null), "total"));
        Assert.Null(FastEngine.ResolveAggregateProjectionField(new AggregateProjection(emptySelection, AggregateOperator.Stdevp, null, null), "total"));
        Assert.Null(FastEngine.ResolveAggregateProjectionField(new AggregateProjection(emptySelection, AggregateOperator.Skew, null, null), "total"));
        Assert.Null(FastEngine.ResolveAggregateProjectionField(new AggregateProjection(emptySelection, AggregateOperator.Kurtosis, null, null), "total"));

        var singleSelection = new AggregateSelectionContext(
            OrdersField,
            isFlat: false,
            CreateProviderQueryable("Microsoft.EntityFrameworkCore.Sqlite", relational: true, source: ExampleData.Customers[0].Orders.Take(1).AsQueryable()));
        Assert.Equal(0d, FastEngine.ResolveAggregateProjectionField(new AggregateProjection(singleSelection, AggregateOperator.Var, null, null), "total"));
        Assert.Equal(0d, FastEngine.ResolveAggregateProjectionField(new AggregateProjection(singleSelection, AggregateOperator.Varp, null, null), "total"));
        Assert.Equal(0d, FastEngine.ResolveAggregateProjectionField(new AggregateProjection(singleSelection, AggregateOperator.Stdev, null, null), "total"));
        Assert.Equal(0d, FastEngine.ResolveAggregateProjectionField(new AggregateProjection(singleSelection, AggregateOperator.Stdevp, null, null), "total"));
        Assert.Equal(0d, FastEngine.ResolveAggregateProjectionField(new AggregateProjection(singleSelection, AggregateOperator.Skew, null, null), "total"));
        Assert.Equal(0d, FastEngine.ResolveAggregateProjectionField(new AggregateProjection(singleSelection, AggregateOperator.Kurtosis, null, null), "total"));
    }

    [Fact]
    public void QueryableMomentHelpers_ShouldCoverUnsupportedAndFailureBranches()
    {
        var ordersModel = Catalog.TryGetObjectType(typeof(Order))!;
        var totalScalar = ordersModel.FindScalar("total")!;
        var referenceScalar = ordersModel.FindScalar("reference")!;
        var couponsModel = Catalog.TryGetObjectType(typeof(Coupon))!;
        var interestRateScalar = couponsModel.FindScalar("interestRate")!;
        var relationalQueryable = CreateProviderQueryable("Microsoft.EntityFrameworkCore.Sqlite", relational: true);
        var relationalSelection = new AggregateSelectionContext(OrdersField, isFlat: false, relationalQueryable);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryResolveQueryableAggregate",
            relationalSelection,
            "total",
            (AggregateOperator)999,
            null,
            out var invalidOperatorArguments)!);
        Assert.Null(invalidOperatorArguments[3]);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryExecuteQueryableMomentAggregate",
            ExampleData.Customers[0].Orders.AsQueryable(),
            totalScalar,
            AggregateOperator.Var,
            null,
            out var nonEfArguments)!);
        Assert.Null(nonEfArguments[3]);

        Assert.False((bool)InvokePrivate(
            StableEngine,
            "TryExecuteQueryableMomentAggregate",
            relationalQueryable,
            totalScalar,
            AggregateOperator.Var,
            null,
            out var stableArguments)!);
        Assert.Null(stableArguments[3]);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryExecuteQueryableMomentAggregate",
            relationalQueryable,
            referenceScalar,
            AggregateOperator.Var,
            null,
            out var nonNumericMomentArguments)!);
        Assert.Null(nonNumericMomentArguments[3]);

        Assert.True((bool)InvokePrivate(
            FastEngine,
            "TryExecuteQueryableMomentAggregate",
            relationalQueryable,
            totalScalar,
            (AggregateOperator)999,
            null,
            out var invalidMomentArguments)!);
        Assert.Null(invalidMomentArguments[3]);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetNativeVarianceMethod",
            EfCoreProviderFamily.RelationalGeneric,
            typeof(decimal),
            false,
            null,
            null,
            out var unsupportedVarianceArguments)!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetNativeVarianceMethod",
            EfCoreProviderFamily.SqlServer,
            typeof(bool),
            false,
            null,
            null,
            out _)!);

        Assert.True((bool)InvokePrivate(
            FastEngine,
            "TryGetNativeVarianceMethod",
            EfCoreProviderFamily.SqlServer,
            typeof(byte),
            false,
            null,
            null,
            out _)!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetNativeStandardDeviationMethod",
            EfCoreProviderFamily.RelationalGeneric,
            typeof(decimal),
            false,
            null,
            null,
            out var unsupportedNativeArguments)!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetNativeStandardDeviationMethod",
            EfCoreProviderFamily.Oracle,
            typeof(decimal),
            false,
            null,
            null,
            out _)!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetNativeStandardDeviationMethod",
            EfCoreProviderFamily.SqlServer,
            typeof(bool),
            false,
            null,
            null,
            out _)!);

        Assert.True((bool)InvokePrivate(
            FastEngine,
            "TryGetNativeStandardDeviationMethod",
            EfCoreProviderFamily.SqlServer,
            typeof(byte),
            false,
            null,
            null,
            out _)!);

        var throwingVarianceQueryable = CreateProviderQueryable(
            "Microsoft.EntityFrameworkCore.SqlServer",
            relational: true,
            interceptor: methodCall => methodCall.Method.Name.StartsWith("Variance", StringComparison.Ordinal)
                ? throw new InvalidOperationException("boom")
                : ServiceBackedQueryProvider.UnhandledExecution.Instance);
        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryExecuteNativeVariance",
            throwingVarianceQueryable,
            totalScalar,
            new EfCoreProviderInfo(true, true, EfCoreProviderFamily.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer"),
            false,
            null,
            out var throwingVarianceArguments)!);
        Assert.Null(throwingVarianceArguments[4]);

        Assert.True((bool)InvokePrivate(
            FastEngine,
            "TryExecuteNativeVariance",
            CreateProviderQueryable("Microsoft.EntityFrameworkCore.SqlServer", relational: true, source: Array.Empty<Order>().AsQueryable()),
            totalScalar,
            new EfCoreProviderInfo(true, true, EfCoreProviderFamily.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer"),
            false,
            null,
            out var emptyVarianceArguments)!);
        Assert.Null(emptyVarianceArguments[4]);

        Assert.True((bool)InvokePrivate(
            FastEngine,
            "TryExecuteNativeVariance",
            CreateProviderQueryable("Microsoft.EntityFrameworkCore.SqlServer", relational: true, source: ExampleData.Customers[0].Orders.Take(1).AsQueryable()),
            totalScalar,
            new EfCoreProviderInfo(true, true, EfCoreProviderFamily.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer"),
            true,
            null,
            out var singleVarianceArguments)!);
        Assert.Equal(0d, singleVarianceArguments[4]);

        Assert.True((bool)InvokePrivate(
            FastEngine,
            "TryExecuteNativeVariance",
            CreateProviderQueryable(
                "Microsoft.EntityFrameworkCore.SqlServer",
                relational: true,
                interceptor: methodCall => methodCall.Method.Name == "VarianceSample"
                    ? ServiceBackedQueryProvider.NullExecution.Instance
                    : ServiceBackedQueryProvider.UnhandledExecution.Instance),
            totalScalar,
            new EfCoreProviderInfo(true, true, EfCoreProviderFamily.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer"),
            false,
            null,
            out var nullVarianceArguments)!);
        Assert.Null(nullVarianceArguments[4]);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryExecuteNativeVariance",
            new ServiceBackedQueryable<Order>(
                ExampleData.Customers[0].Orders.AsQueryable(),
                CreateProviderServices("Microsoft.EntityFrameworkCore.Sqlite", relational: true),
                interceptor: null,
                throwOnCreateQuery: true),
            totalScalar,
            new EfCoreProviderInfo(true, true, EfCoreProviderFamily.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer"),
            false,
            null,
            out _)!);

        var throwingNativeQueryable = CreateProviderQueryable(
            "Microsoft.EntityFrameworkCore.SqlServer",
            relational: true,
            interceptor: methodCall => methodCall.Method.Name.StartsWith("StandardDeviation", StringComparison.Ordinal)
                ? throw new InvalidOperationException("boom")
                : ServiceBackedQueryProvider.UnhandledExecution.Instance);
        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryExecuteNativeStandardDeviation",
            throwingNativeQueryable,
            totalScalar,
            new EfCoreProviderInfo(true, true, EfCoreProviderFamily.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer"),
            false,
            null,
            out var throwingNativeArguments)!);
        Assert.Null(throwingNativeArguments[4]);

        Assert.True((bool)InvokePrivate(
            FastEngine,
            "TryExecuteNativeStandardDeviation",
            CreateProviderQueryable("Microsoft.EntityFrameworkCore.SqlServer", relational: true, source: Array.Empty<Order>().AsQueryable()),
            totalScalar,
            new EfCoreProviderInfo(true, true, EfCoreProviderFamily.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer"),
            false,
            null,
            out var emptyNativeArguments)!);
        Assert.Null(emptyNativeArguments[4]);

        Assert.True((bool)InvokePrivate(
            FastEngine,
            "TryExecuteNativeStandardDeviation",
            CreateProviderQueryable("Microsoft.EntityFrameworkCore.SqlServer", relational: true, source: ExampleData.Customers[0].Orders.Take(1).AsQueryable()),
            totalScalar,
            new EfCoreProviderInfo(true, true, EfCoreProviderFamily.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer"),
            true,
            null,
            out var singleNativeArguments)!);
        Assert.Equal(0d, singleNativeArguments[4]);

        Assert.True((bool)InvokePrivate(
            FastEngine,
            "TryExecuteNativeStandardDeviation",
            CreateProviderQueryable(
                "Microsoft.EntityFrameworkCore.SqlServer",
                relational: true,
                interceptor: methodCall => methodCall.Method.Name == "StandardDeviationSample"
                    ? ServiceBackedQueryProvider.NullExecution.Instance
                    : ServiceBackedQueryProvider.UnhandledExecution.Instance),
            totalScalar,
            new EfCoreProviderInfo(true, true, EfCoreProviderFamily.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer"),
            false,
            null,
            out var nullNativeArguments)!);
        Assert.Null(nullNativeArguments[4]);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryCalculateQueryableMomentStatistics",
            relationalQueryable,
            referenceScalar,
            null,
            out _)!);

        var averageThrowingQueryable = CreateProviderQueryable(
            "Microsoft.EntityFrameworkCore.Sqlite",
            relational: true,
            interceptor: methodCall => methodCall.Method.Name == "Average"
                ? throw new InvalidOperationException("average")
                : ServiceBackedQueryProvider.UnhandledExecution.Instance);
        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryExecuteQueryableMomentAggregate",
            averageThrowingQueryable,
            totalScalar,
            AggregateOperator.Skew,
            null,
            out _)!);
        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryCalculateQueryableMomentStatistics",
            averageThrowingQueryable,
            totalScalar,
            null,
            out _)!);

        var createQueryThrowingQueryable = new ServiceBackedQueryable<Order>(
            ExampleData.Customers[0].Orders.AsQueryable(),
            CreateProviderServices("Microsoft.EntityFrameworkCore.Sqlite", relational: true),
            interceptor: null,
            throwOnCreateQuery: true);
        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryCreateQueryableProjection",
            createQueryThrowingQueryable,
            totalScalar,
            typeof(double),
            null,
            out _)!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryCreateQueryableProjection",
            relationalQueryable,
            referenceScalar,
            typeof(double),
            null,
            out _)!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryExecuteNativeStandardDeviation",
            createQueryThrowingQueryable,
            totalScalar,
            new EfCoreProviderInfo(true, true, EfCoreProviderFamily.SqlServer, "Microsoft.EntityFrameworkCore.SqlServer"),
            false,
            null,
            out _)!);

        var nullableNumericQueryable = new ServiceBackedQueryable<Coupon>(
            ExampleData.Securities[2].Details.Coupons.AsQueryable(),
            CreateProviderServices("Microsoft.EntityFrameworkCore.Sqlite", relational: true));
        Assert.True((bool)InvokePrivate(
            FastEngine,
            "TryCreateQueryableProjection",
            nullableNumericQueryable,
            interestRateScalar,
            typeof(double),
            null,
            out _)!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetQueryableProjectedNumericValues",
            new AggregateSelectionContext(OrdersField, isFlat: true, relationalQueryable),
            "total",
            null,
            out var flatProjectedArguments)!);
        Assert.Empty((double[])flatProjectedArguments[2]!);

        var textRowsField = new CollectionFieldModel(
            typeof(TextRowsHost).GetProperty(nameof(TextRowsHost.Rows))!,
            "rows",
            typeof(string[]),
            typeof(string),
            "Query",
            "String");
        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetQueryableProjectedNumericValues",
            new AggregateSelectionContext(textRowsField, isFlat: false, new[] { "A", "B" }.AsQueryable()),
            "value",
            null,
            out var noModelProjectedArguments)!);
        Assert.Empty((double[])noModelProjectedArguments[2]!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetQueryableProjectedNumericValues",
            relationalSelection,
            "missing",
            null,
            out var missingFieldProjectedArguments)!);
        Assert.Empty((double[])missingFieldProjectedArguments[2]!);

        Assert.False((bool)InvokePrivate(
            FastEngine,
            "TryGetQueryableProjectedNumericValues",
            new AggregateSelectionContext(
                OrdersField,
                isFlat: false,
                CreateProviderQueryable(
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    relational: true,
                    throwOnEnumerationTypes: [typeof(double)])),
            "total",
            null,
            out var throwingProjectedArguments)!);
        Assert.Empty((double[])throwingProjectedArguments[2]!);
    }

    private static IQueryable<Order> CreateProviderQueryable(
        string? providerName,
        bool relational,
        IQueryable<Order>? source = null,
        Func<MethodCallExpression, object?>? interceptor = null,
        bool throwOnCreateQuery = false,
        bool throwOnSourceEnumeration = false,
        Type[]? throwOnEnumerationTypes = null)
    {
        source ??= ExampleData.Customers[0].Orders.AsQueryable();

        return new ServiceBackedQueryable<Order>(
            source,
            CreateProviderServices(providerName, relational),
            interceptor,
            throwOnCreateQuery,
            throwOnSourceEnumeration,
            throwOnEnumerationTypes);
    }

    private static object? ResolveRowAggregate(AggregateOperator aggregateOperator)
    {
        var rowSelection = new AggregateSelectionContext(
            OrdersField,
            isFlat: false,
            ExampleData.Customers[0].Orders.Cast<object>().ToArray());
        return FastEngine.ResolveAggregateProjectionField(new AggregateProjection(rowSelection, aggregateOperator, null, null), "total");
    }

    private static IServiceProvider CreateProviderServices(string? providerName, bool relational)
    {
        var services = new ServiceCollection();

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            services.AddSingleton<IDatabaseProvider>(new FakeDatabaseProvider(providerName));
        }

        if (relational)
        {
            services.AddSingleton(typeof(IRelationalConnection), ReflectionTestSupport.Create<IRelationalConnection>((method, _) =>
                method.ReturnType.IsValueType
                    ? RuntimeHelpers.GetUninitializedObject(method.ReturnType)
                    : null));
        }

        return services.BuildServiceProvider();
    }

    private static void AssertAggregateEquivalent(object? expected, object? actual)
    {
        if (expected is double expectedDouble && actual is double actualDouble)
        {
            Assert.Equal(expectedDouble, actualDouble, 12);
            return;
        }

        Assert.Equal(expected, actual);
    }

    private static object? InvokePrivate(
        object instance,
        string methodName,
        object? arg0,
        object? arg1,
        object? arg2,
        out object?[] arguments)
    {
        arguments = [arg0, arg1, arg2];
        var argumentCount = arguments.Length;
        var method = instance.GetType()
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == argumentCount);
        return method.Invoke(method.IsStatic ? null : instance, arguments);
    }

    private static object? InvokePrivate(
        object instance,
        string methodName,
        object? arg0,
        object? arg1,
        object? arg2,
        object? arg3,
        out object?[] arguments)
    {
        arguments = [arg0, arg1, arg2, arg3];
        var argumentCount = arguments.Length;
        var method = instance.GetType()
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == argumentCount);
        return method.Invoke(method.IsStatic ? null : instance, arguments);
    }

    private static object? InvokePrivate(
        object instance,
        string methodName,
        object? arg0,
        object? arg1,
        object? arg2,
        object? arg3,
        object? arg4,
        out object?[] arguments)
    {
        arguments = [arg0, arg1, arg2, arg3, arg4];
        var argumentCount = arguments.Length;
        var method = instance.GetType()
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == argumentCount);
        return method.Invoke(method.IsStatic ? null : instance, arguments);
    }

    private static object? InvokePrivate(
        object instance,
        string methodName,
        object? arg0,
        object? arg1,
        object? arg2,
        object? arg3,
        object? arg4,
        object? arg5,
        out object?[] arguments)
    {
        arguments = [arg0, arg1, arg2, arg3, arg4, arg5];
        var argumentCount = arguments.Length;
        var method = instance.GetType()
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == argumentCount);
        return method.Invoke(method.IsStatic ? null : instance, arguments);
    }

    private static CollectionFieldModel GetCollectionField<THost>(string fieldName)
    {
        var model = Catalog.TryGetObjectType(typeof(THost));
        Assert.NotNull(model);
        var field = model.FindCollection(fieldName);
        Assert.NotNull(field);
        return field;
    }

    private sealed class FakeDatabaseProvider(string name) : IDatabaseProvider
    {
        public string Name => name;

        public string? Version => "9.0.0";

        public bool IsConfigured(Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptions options) => true;
    }

    private sealed class ServiceBackedQueryable<T>(
        IQueryable<T> inner,
        IServiceProvider services,
        Func<MethodCallExpression, object?>? interceptor = null,
        bool throwOnCreateQuery = false,
        bool throwOnSourceEnumeration = false,
        Type[]? throwOnEnumerationTypes = null) : IOrderedQueryable<T>, IInfrastructure<IServiceProvider>
    {
        private readonly IQueryable<T> _inner = inner;
        private readonly ServiceBackedQueryProvider _provider = new(inner.Provider, services, interceptor, throwOnCreateQuery, throwOnSourceEnumeration, throwOnEnumerationTypes);

        public Type ElementType => typeof(T);

        public Expression Expression => _inner.Expression;

        public IQueryProvider Provider => _provider;

        IServiceProvider IInfrastructure<IServiceProvider>.Instance => _provider.Instance;

        public IEnumerator<T> GetEnumerator() =>
            (throwOnSourceEnumeration && typeof(T) == typeof(Order)) ||
            (throwOnEnumerationTypes?.Contains(typeof(T)) ?? false)
                ? throw new InvalidOperationException("source enumeration")
                : _inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ServiceBackedQueryProvider(
        IQueryProvider inner,
        IServiceProvider services,
        Func<MethodCallExpression, object?>? interceptor,
        bool throwOnCreateQuery = false,
        bool throwOnSourceEnumeration = false,
        Type[]? throwOnEnumerationTypes = null) : IQueryProvider, IInfrastructure<IServiceProvider>
    {
        public sealed class UnhandledExecution
        {
            public static UnhandledExecution Instance { get; } = new();
        }

        public sealed class NullExecution
        {
            public static NullExecution Instance { get; } = new();
        }

        public IServiceProvider Instance => services;

        public IQueryable CreateQuery(Expression expression)
        {
            if (throwOnCreateQuery)
            {
                throw new InvalidOperationException("create query");
            }

            var query = inner.CreateQuery(expression);
            var wrapperType = typeof(ServiceBackedQueryable<>).MakeGenericType(query.ElementType);
            return (IQueryable)Activator.CreateInstance(wrapperType, query, services, interceptor, throwOnCreateQuery, throwOnSourceEnumeration, throwOnEnumerationTypes)!;
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            throwOnCreateQuery
                ? throw new InvalidOperationException("create query")
                : new ServiceBackedQueryable<TElement>(inner.CreateQuery<TElement>(expression), services, interceptor, throwOnCreateQuery, throwOnSourceEnumeration, throwOnEnumerationTypes);

        public object? Execute(Expression expression)
        {
            if (expression is MethodCallExpression methodCall && interceptor is not null)
            {
                var intercepted = interceptor.Invoke(methodCall);
                if (intercepted is NullExecution)
                {
                    return null;
                }

                if (intercepted is not UnhandledExecution)
                {
                    return intercepted;
                }
            }

            return inner.Execute(expression);
        }

        public TResult Execute<TResult>(Expression expression)
        {
            if (expression is MethodCallExpression methodCall && interceptor is not null)
            {
                var intercepted = interceptor.Invoke(methodCall);
                if (intercepted is NullExecution)
                {
                    return default!;
                }

                if (intercepted is not UnhandledExecution)
                {
                    return intercepted is null
                        ? default!
                        : (TResult)intercepted;
                }
            }

            return inner.Execute<TResult>(expression);
        }
    }

    private sealed class TextRowsHost
    {
        public string[] Rows { get; init; } = [];
    }
}
