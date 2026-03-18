using System.Reflection;
using HotChocolate.Execution;
using HotChocolate.Extension.CollectionEnhancements.Generated;
using HotChocolate.Extension.CollectionEnhancements.Metadata;
using HotChocolate.Extension.CollectionEnhancements.Execution;
using HotChocolate.Extension.CollectionEnhancements.Schema;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

public sealed class GeneratedProviderCoverageTests
{
    [Fact]
    public void CollectionSchemaCatalog_ShouldPopulateGeneratedModels_AndHandleEarlyReturnPaths()
    {
        var catalog = new CollectionSchemaCatalog();
        var generatedType = CreateGeneratedObjectType();

        ReflectionTestSupport.InvokeInstance(catalog, "TryPopulateGeneratedModel", generatedType);

        var model = Assert.IsType<ObjectTypeModel>(
            ReflectionTestSupport.InvokeInstance(
                catalog,
                "GetOrCreateModel",
                typeof(GeneratedProviderCoverageHost),
                "GeneratedProviderCoverageHost",
                false)!);

        ReflectionTestSupport.InvokeInstance(catalog, "TryPopulateGeneratedModel", generatedType);

        Assert.NotNull(model.FindScalar("id"));
        Assert.NotNull(model.FindObject("child"));
        var collection = model.FindCollection("items");
        Assert.NotNull(collection);
        Assert.Equal(typeof(GeneratedProviderCoverageFlatRow), collection.GeneratedFlatRowClrType);

        var scalarCount = model.ScalarFields.Count;
        ReflectionTestSupport.InvokeInstance(catalog, "TryPopulateGeneratedModel", generatedType);
        Assert.Equal(scalarCount, model.ScalarFields.Count);

        var sameModel = Assert.IsType<ObjectTypeModel>(
            ReflectionTestSupport.InvokeInstance(
                catalog,
                "GetOrCreateModel",
                typeof(GeneratedProviderCoverageHost),
                "OtherNameShouldBeIgnored",
                false)!);
        Assert.Same(model, sameModel);
    }

    [Fact]
    public void CollectionSchemaCatalog_ShouldRejectInvalidGeneratedMemberDefinitions()
    {
        AssertInvalidResolution(
            "TryResolveGeneratedScalarFields",
            typeof(GeneratedProviderCoverageHost),
            new CollectionEnhancementGeneratedScalarField[]
            {
                new("Missing", CollectionEnhancementGeneratedMemberKind.Property, "missing", typeof(int))
            });

        AssertInvalidResolution(
            "TryResolveGeneratedObjectFields",
            typeof(GeneratedProviderCoverageHost),
            new CollectionEnhancementGeneratedObjectField[]
            {
                new("Missing", CollectionEnhancementGeneratedMemberKind.Property, "missing", typeof(GeneratedProviderCoverageChild))
            });

        var collectionMethod = typeof(CollectionSchemaCatalog).GetMethod(
            "TryResolveGeneratedCollectionFields",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var collectionArguments = new object?[]
        {
            new CollectionEnhancementGeneratedObjectType(
                typeof(GeneratedProviderCoverageHost),
                "GeneratedProviderCoverageHost",
                false,
                [],
                [],
                [
                    new CollectionEnhancementGeneratedCollectionField(
                        "Missing",
                        CollectionEnhancementGeneratedMemberKind.Method,
                        "items",
                        typeof(IReadOnlyList<GeneratedProviderCoverageLeaf>),
                        typeof(GeneratedProviderCoverageLeaf),
                        "GeneratedProviderCoverageHost",
                        "GeneratedProviderCoverageLeaf",
                        typeof(GeneratedProviderCoverageFlatRow),
                        [])
                ]),
            null
        };

        Assert.False((bool)collectionMethod.Invoke(null, collectionArguments)!);
        Assert.Empty(Assert.IsType<List<CollectionFieldModel>>(collectionArguments[1]));
    }

    [Fact]
    public void CollectionSchemaCatalog_ShouldLeaveGeneratedModelsUnchanged_WhenPopulationFails()
    {
        var catalog = new CollectionSchemaCatalog();
        var model = Assert.IsType<ObjectTypeModel>(
            ReflectionTestSupport.InvokeInstance(
                catalog,
                "GetOrCreateModel",
                typeof(GeneratedProviderCoverageHost),
                "GeneratedProviderCoverageHost",
                false)!);

        var invalidGeneratedType = new CollectionEnhancementGeneratedObjectType(
            typeof(GeneratedProviderCoverageHost),
            "GeneratedProviderCoverageHost",
            false,
            [
                new CollectionEnhancementGeneratedScalarField(
                    nameof(GeneratedProviderCoverageHost.Id),
                    CollectionEnhancementGeneratedMemberKind.Property,
                    "id",
                    typeof(int))
            ],
            [
                new CollectionEnhancementGeneratedObjectField(
                    "MissingChild",
                    CollectionEnhancementGeneratedMemberKind.Property,
                    "child",
                    typeof(GeneratedProviderCoverageChild))
            ],
            []);

        ReflectionTestSupport.InvokeInstance(catalog, "TryPopulateGeneratedModel", invalidGeneratedType);

        Assert.Empty(model.ScalarFields);
        Assert.Empty(model.ObjectFields);
        Assert.Empty(model.CollectionFields);
    }

    [Fact]
    public void CollectionSchemaCatalog_ShouldCoverGeneratedMemberAndProviderHelpers()
    {
        var tryResolveMember = typeof(CollectionSchemaCatalog).GetMethod(
            "TryResolveMember",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var propertyArguments = new object?[]
        {
            typeof(GeneratedProviderCoverageHost),
            nameof(GeneratedProviderCoverageHost.Id),
            CollectionEnhancementGeneratedMemberKind.Property,
            null
        };
        Assert.True((bool)tryResolveMember.Invoke(null, propertyArguments)!);
        Assert.IsAssignableFrom<PropertyInfo>(propertyArguments[3]);

        var methodArguments = new object?[]
        {
            typeof(GeneratedProviderCoverageHost),
            nameof(GeneratedProviderCoverageHost.GetItems),
            CollectionEnhancementGeneratedMemberKind.Method,
            null
        };
        Assert.True((bool)tryResolveMember.Invoke(null, methodArguments)!);
        Assert.IsAssignableFrom<MethodInfo>(methodArguments[3]);

        var invalidArguments = new object?[]
        {
            typeof(GeneratedProviderCoverageHost),
            "Missing",
            (CollectionEnhancementGeneratedMemberKind)99,
            null
        };
        Assert.False((bool)tryResolveMember.Invoke(null, invalidArguments)!);
        Assert.Null(invalidArguments[3]);

        var tryCreateProvider = typeof(CollectionSchemaCatalog).GetMethod(
            "TryCreateProvider",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Null(tryCreateProvider.Invoke(null, [typeof(ThrowingGeneratedModelProvider)]));

        var isQueryType = typeof(CollectionSchemaCatalog).GetMethod(
            "IsQueryType",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True((bool)isQueryType.Invoke(null, [typeof(Query)])!);
        Assert.False((bool)isQueryType.Invoke(null, [typeof(GeneratedProviderCoverageHost)])!);
    }

    [Fact]
    public void CollectionSchemaCatalog_ShouldNotDuplicateGeneratedModels_WhenReflectionPopulationRunsAfterGeneratedPopulation()
    {
        var catalog = new CollectionSchemaCatalog();
        var generatedType = CreateGeneratedObjectType();

        var model = Assert.IsType<ObjectTypeModel>(
            ReflectionTestSupport.InvokeInstance(
                catalog,
                "GetOrCreateModel",
                typeof(GeneratedProviderCoverageHost),
                "GeneratedProviderCoverageHost",
                false)!);

        ReflectionTestSupport.InvokeInstance(catalog, "TryPopulateGeneratedModel", generatedType);
        ReflectionTestSupport.InvokeInstance(catalog, "PopulateModel", model);

        Assert.NotNull(model);
        Assert.Single(model.ScalarFields);
        Assert.Single(model.ObjectFields);
        Assert.Single(model.CollectionFields);

        var collection = Assert.Single(model.CollectionFields);
        Assert.Equal(typeof(GeneratedProviderCoverageFlatRow), collection.GeneratedFlatRowClrType);
    }

    [Fact]
    public async Task Registrar_ShouldBuildSchema_AndExecute_For_GeneratedPopulatedModels_WhenReflectionPopulationIsSkipped()
    {
        var catalog = new CollectionSchemaCatalog();
        var queryModel = Assert.IsType<ObjectTypeModel>(
            ReflectionTestSupport.InvokeInstance(catalog, "GetOrCreateModel", typeof(GeneratedProviderRuntimeQuery), true)!);
        ReflectionTestSupport.InvokeInstance(catalog, "PopulateModel", queryModel);

        var generatedType = CreateRuntimeSchemaGeneratedObjectType();
        var hostModel = Assert.IsType<ObjectTypeModel>(
            ReflectionTestSupport.InvokeInstance(
                catalog,
                "GetOrCreateModel",
                typeof(GeneratedProviderRuntimeHost),
                "GeneratedProviderRuntimeHost",
                false)!);

        ReflectionTestSupport.InvokeInstance(catalog, "TryPopulateGeneratedModel", generatedType);
        ReflectionTestSupport.InvokeInstance(catalog, "PopulateModel", hostModel);

        var services = new ServiceCollection();
        var options = new CollectionEnhancementOptions();
        services.AddSingleton(catalog);
        services.AddSingleton(options);
        services.AddSingleton(sp => new CollectionExecutionEngine(
            sp.GetRequiredService<CollectionSchemaCatalog>(),
            sp.GetRequiredService<CollectionEnhancementOptions>()));

        var builder = services
            .AddGraphQLServer()
            .AddQueryType<GeneratedProviderRuntimeQuery>(descriptor => descriptor.Name("Query"))
            .AddType<GeneratedProviderRuntimeHost>()
            .AddType<GeneratedProviderRuntimeChild>()
            .AddType<GeneratedProviderRuntimeLeaf>()
            .AddCostAnalyzer()
            .ModifyCostOptions(costOptions =>
            {
                costOptions.MaxFieldCost = 500_000;
                costOptions.MaxTypeCost = 500_000;
                costOptions.EnforceCostLimits = false;
                costOptions.ApplyCostDefaults = true;
                costOptions.ApplySlicingArgumentDefaultValue = true;
            })
            .AddProjections()
            .AddFiltering()
            .AddSorting();
        builder.AddHttpRequestInterceptor<CollectionEnhancementHttpRequestInterceptor>();
        services.AddHttpResponseFormatter<CollectionEnhancementHttpResponseFormatter>();

        new CollectionEnhancementTypeRegistrar(catalog).Register(builder);

        await using var serviceProvider = services.BuildServiceProvider();
        var executor = await serviceProvider.GetRequiredService<IRequestExecutorResolver>().GetRequestExecutorAsync();
        var result = await executor.ExecuteQueryResultAsync("""
            query {
              generatedHosts {
                id
                child {
                  name
                }
                items {
                  value
                }
              }
            }
            """);

        using var json = result.AssertSuccessfulJson();
        var row = json.RootElement
            .GetProperty("data")
            .GetProperty("generatedHosts")[0];
        Assert.Equal(7, row.GetProperty("id").GetInt32());
        Assert.Equal("child", row.GetProperty("child").GetProperty("name").GetString());
        Assert.Equal(1, row.GetProperty("items")[0].GetProperty("value").GetInt32());
    }

    [Fact]
    public void GeneratedModelContracts_ShouldExposeRecordValues()
    {
        var scalar = new CollectionEnhancementGeneratedScalarField(
            nameof(GeneratedProviderCoverageHost.Id),
            CollectionEnhancementGeneratedMemberKind.Property,
            "id",
            typeof(int));
        var child = new CollectionEnhancementGeneratedObjectField(
            nameof(GeneratedProviderCoverageHost.Child),
            CollectionEnhancementGeneratedMemberKind.Property,
            "child",
            typeof(GeneratedProviderCoverageChild));
        var flatPath = new CollectionEnhancementGeneratedFlatPath(
            "items",
            "item",
            typeof(GeneratedProviderCoverageLeaf),
            nameof(GeneratedProviderCoverageLeaf),
            [nameof(GeneratedProviderCoverageHost.GetItems)]);
        var collection = new CollectionEnhancementGeneratedCollectionField(
            nameof(GeneratedProviderCoverageHost.GetItems),
            CollectionEnhancementGeneratedMemberKind.Method,
            "items",
            typeof(IReadOnlyList<GeneratedProviderCoverageLeaf>),
            typeof(GeneratedProviderCoverageLeaf),
            nameof(GeneratedProviderCoverageHost),
            nameof(GeneratedProviderCoverageLeaf),
            typeof(GeneratedProviderCoverageFlatRow),
            [flatPath]);
        var generatedType = new CollectionEnhancementGeneratedObjectType(
            typeof(GeneratedProviderCoverageHost),
            nameof(GeneratedProviderCoverageHost),
            false,
            [scalar],
            [child],
            [collection]);

        Assert.Equal("id", scalar.GraphQlName);
        Assert.Equal("child", child.GraphQlName);
        Assert.Equal("items", flatPath.Path);
        Assert.Equal("item", flatPath.Prefix);
        Assert.Equal(typeof(GeneratedProviderCoverageLeaf), flatPath.TerminalElementType);
        Assert.Equal(nameof(GeneratedProviderCoverageLeaf), flatPath.TerminalTypeName);
        Assert.Equal([nameof(GeneratedProviderCoverageHost.GetItems)], flatPath.SegmentMemberNames);
        Assert.Equal(typeof(GeneratedProviderCoverageFlatRow), collection.FlatRowClrType);
        Assert.Equal(nameof(GeneratedProviderCoverageHost), collection.HostTypeName);
        Assert.Equal(nameof(GeneratedProviderCoverageLeaf), collection.ElementTypeName);
        Assert.Equal(typeof(GeneratedProviderCoverageHost), generatedType.ClrType);
        Assert.False(generatedType.IsQueryRoot);
        Assert.Same(scalar, generatedType.ScalarFields.Single());
        Assert.Same(child, generatedType.ObjectFields.Single());
        Assert.Same(collection, generatedType.CollectionFields.Single());
    }

    private static void AssertInvalidResolution(string methodName, Type declaringType, object generatedFields)
    {
        var method = typeof(CollectionSchemaCatalog).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var arguments = new object?[] { declaringType, generatedFields, null };

        Assert.False((bool)method.Invoke(null, arguments)!);
        Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(arguments[2]));
    }

    private static CollectionEnhancementGeneratedObjectType CreateGeneratedObjectType() =>
        new(
            typeof(GeneratedProviderCoverageHost),
            "GeneratedProviderCoverageHost",
            false,
            [
                new CollectionEnhancementGeneratedScalarField(
                    nameof(GeneratedProviderCoverageHost.Id),
                    CollectionEnhancementGeneratedMemberKind.Property,
                    "id",
                    typeof(int))
            ],
            [
                new CollectionEnhancementGeneratedObjectField(
                    nameof(GeneratedProviderCoverageHost.Child),
                    CollectionEnhancementGeneratedMemberKind.Property,
                    "child",
                    typeof(GeneratedProviderCoverageChild))
            ],
            [
                new CollectionEnhancementGeneratedCollectionField(
                    nameof(GeneratedProviderCoverageHost.GetItems),
                    CollectionEnhancementGeneratedMemberKind.Method,
                    "items",
                    typeof(IReadOnlyList<GeneratedProviderCoverageLeaf>),
                    typeof(GeneratedProviderCoverageLeaf),
                    "GeneratedProviderCoverageHost",
                    "GeneratedProviderCoverageLeaf",
                    typeof(GeneratedProviderCoverageFlatRow),
                    [])
            ]);

    private static CollectionEnhancementGeneratedObjectType CreateRuntimeSchemaGeneratedObjectType() =>
        new(
            typeof(GeneratedProviderRuntimeHost),
            "GeneratedProviderRuntimeHost",
            false,
            [
                new CollectionEnhancementGeneratedScalarField(
                    nameof(GeneratedProviderRuntimeHost.Id),
                    CollectionEnhancementGeneratedMemberKind.Property,
                    "id",
                    typeof(int))
            ],
            [
                new CollectionEnhancementGeneratedObjectField(
                    nameof(GeneratedProviderRuntimeHost.Child),
                    CollectionEnhancementGeneratedMemberKind.Property,
                    "child",
                    typeof(GeneratedProviderRuntimeChild))
            ],
            [
                new CollectionEnhancementGeneratedCollectionField(
                    nameof(GeneratedProviderRuntimeHost.GetItems),
                    CollectionEnhancementGeneratedMemberKind.Method,
                    "items",
                    typeof(IReadOnlyList<GeneratedProviderRuntimeLeaf>),
                    typeof(GeneratedProviderRuntimeLeaf),
                    "GeneratedProviderRuntimeHost",
                    "GeneratedProviderRuntimeLeaf",
                    typeof(GeneratedProviderRuntimeFlatRow),
                    [])
            ]);

    public sealed record GeneratedProviderCoverageChild(string Name);

    public sealed record GeneratedProviderCoverageLeaf(int Value);

    public sealed record GeneratedProviderCoverageFlatRow;

    public sealed class GeneratedProviderCoverageHost
    {
        public int Id => 7;

        public GeneratedProviderCoverageChild Child => new("child");

        public IReadOnlyList<GeneratedProviderCoverageLeaf> GetItems() =>
            [new(1)];
    }

    public sealed class GeneratedProviderSchemaQuery
    {
        public GeneratedProviderCoverageHost[] GetGeneratedHosts() =>
            [new()];
    }

    private sealed class ThrowingGeneratedModelProvider : ICollectionEnhancementGeneratedModelProvider
    {
        public ThrowingGeneratedModelProvider() =>
            throw new InvalidOperationException("boom");

        public IReadOnlyList<CollectionEnhancementGeneratedObjectType> GetObjectTypes() => [];
    }
}

public sealed record GeneratedProviderRuntimeChild(string Name);

public sealed record GeneratedProviderRuntimeLeaf(int Value);

public sealed record GeneratedProviderRuntimeFlatRow;

public sealed class GeneratedProviderRuntimeHost
{
    public int Id => 7;

    public GeneratedProviderRuntimeChild Child => new("child");

    public IReadOnlyList<GeneratedProviderRuntimeLeaf> GetItems() =>
        [new(1)];
}

public sealed class GeneratedProviderRuntimeQuery
{
    public GeneratedProviderRuntimeHost[] GetGeneratedHosts() =>
        [new()];
}
