using System.Reflection;
using HotChocolate.Extension.CollectionEnhancements.Generated;
using HotChocolate.Extension.CollectionEnhancements.Metadata;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;

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

    private sealed record GeneratedProviderCoverageChild(string Name);

    private sealed record GeneratedProviderCoverageLeaf(int Value);

    private sealed record GeneratedProviderCoverageFlatRow;

    private sealed class GeneratedProviderCoverageHost
    {
        public int Id => 7;

        public GeneratedProviderCoverageChild Child => new("child");

        public IReadOnlyList<GeneratedProviderCoverageLeaf> GetItems() =>
            [new(1)];
    }

    private sealed class ThrowingGeneratedModelProvider : ICollectionEnhancementGeneratedModelProvider
    {
        public ThrowingGeneratedModelProvider() =>
            throw new InvalidOperationException("boom");

        public IReadOnlyList<CollectionEnhancementGeneratedObjectType> GetObjectTypes() => [];
    }
}
