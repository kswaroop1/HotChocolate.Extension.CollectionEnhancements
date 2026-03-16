using System.Collections.Immutable;
using System.Reflection;
using HotChocolate.Extension.CollectionEnhancements;
using HotChocolate.Extension.CollectionEnhancements.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

public sealed class SourceGeneratorCoverageTests
{
    [Fact]
    public void IncrementalGenerator_ShouldGenerateProviderAndFlatRowTypes_ForQueryRoots()
    {
        var compilation = CreateCompilation(
            """
            using System;
            using System.Collections.Generic;
            using HotChocolate.Extension.CollectionEnhancements;

            public sealed class ServiceAttribute : Attribute { }

            public sealed record Coupon(DateOnly PaymentDate, string Label);
            public sealed record BranchA(IReadOnlyList<Coupon> Coupons);
            public sealed record BranchB(IReadOnlyList<Coupon> Coupons);
            public sealed record Details(BranchA BranchA, BranchB BranchB);
            public sealed record Security(int Id, string Isin, Details Details);

            public sealed class Query
            {
                public IReadOnlyList<Security> GetSecurities([Service] object services) => Array.Empty<Security>();
                public IReadOnlyList<Security> Unsupported(int skip) => Array.Empty<Security>();
            }
            """);

        GeneratorDriver driver = CreateDriver(compilation);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var runResult = driver.GetRunResult();
        var generatedSource = Assert.Single(Assert.Single(runResult.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("CollectionEnhancementGeneratedModelProvider", generatedSource, StringComparison.Ordinal);
        Assert.Contains("public sealed partial record class QuerySecuritiesGeneratedFlatRow", generatedSource, StringComparison.Ordinal);
        Assert.Contains("public global::System.String? Isin { get; init; }", generatedSource, StringComparison.Ordinal);
        Assert.Contains("public global::System.String? BranchACouponLabel { get; init; }", generatedSource, StringComparison.Ordinal);
        Assert.Contains("public global::System.String? BranchBCouponLabel { get; init; }", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Unsupported", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IncrementalGenerator_ShouldSkipOutput_WhenNoRootsArePresent()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;

            public sealed record PlainValue(int Id);
            """);

        GeneratorDriver driver = CreateDriver(compilation);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(Assert.Single(driver.GetRunResult().Results).GeneratedSources);
        Assert.Empty(CollectionEnhancementSourceGenerator.ModelDiscovery.Discover(compilation).ObjectTypes);
    }

    [Fact]
    public void ModelDiscovery_ShouldSupportAttributedRoots_And_SourceEmitter_ShouldHandleUnknownTerminalTypes()
    {
        var attributedCompilation = CreateCompilation(
            """
            using System;
            using System.Collections.Generic;
            using HotChocolate.Extension.CollectionEnhancements;

            [CollectionEnhancementModel(IsQueryRoot = true)]
            public sealed class CatalogQuery
            {
                public IReadOnlyList<CatalogItem> GetItems() => Array.Empty<CatalogItem>();
            }

            [CollectionEnhancementModel]
            public sealed class AuxiliaryRoot
            {
                public string Name => "aux";
            }

            public sealed record CatalogItem(int Id, string Title);
            """);

        var discoveredModel = CollectionEnhancementSourceGenerator.ModelDiscovery.Discover(attributedCompilation);
        var queryRoot = Assert.Single(discoveredModel.ObjectTypes.Where(type => type.IsQueryRoot));
        var auxiliaryRoot = Assert.Single(discoveredModel.ObjectTypes.Where(type => type.GraphQlTypeName == "AuxiliaryRoot"));

        Assert.Equal("Query", queryRoot.GraphQlTypeName);
        Assert.Single(queryRoot.CollectionFields);
        Assert.Empty(queryRoot.CollectionFields[0].FlatPaths);
        Assert.False(auxiliaryRoot.IsQueryRoot);

        var manualFlatPath = new CollectionEnhancementSourceGenerator.ModelDiscovery.FlatPathInfo(
            "items.tags",
            "itemTag",
            "global::Demo.Tag",
            "Tag",
            ImmutableArray.Create("Items", "Tags"));
        var manualCollectionField = new CollectionEnhancementSourceGenerator.ModelDiscovery.CollectionFieldInfo(
            "GetRows",
            CollectionEnhancementSourceGenerator.ModelDiscovery.MemberKindInfo.Method,
            "rows",
            "global::System.Collections.Generic.IEnumerable<global::Demo.Row>",
            "global::Demo.Row",
            "Query",
            "Row",
            "QueryRowsGeneratedFlatRow",
            ImmutableArray.Create(manualFlatPath));
        var manualRowScalar = new CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo(
            "Id",
            CollectionEnhancementSourceGenerator.ModelDiscovery.MemberKindInfo.Property,
            "id",
            "global::System.Int32",
            "global::System.Int32");
        var manualModel = new CollectionEnhancementSourceGenerator.ModelDiscovery.GenerationModel(
            ImmutableArray.Create(
                new CollectionEnhancementSourceGenerator.ModelDiscovery.ObjectTypeInfo(
                    "global::Demo.Query",
                    "Query",
                    true,
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo>.Empty,
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo>.Empty,
                    ImmutableArray.Create(manualCollectionField)),
                new CollectionEnhancementSourceGenerator.ModelDiscovery.ObjectTypeInfo(
                    "global::Demo.Row",
                    "Row",
                    false,
                    ImmutableArray.Create(manualRowScalar),
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo>.Empty,
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.CollectionFieldInfo>.Empty)));

        var emittedSource = CollectionEnhancementSourceGenerator.SourceEmitter.Emit(manualModel);
        Assert.Contains("public sealed partial record class QueryRowsGeneratedFlatRow", emittedSource, StringComparison.Ordinal);
        Assert.Contains("public global::System.Int32 Id { get; init; }", emittedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemTag", emittedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_InternalHelpers_ShouldCoverRemainingBranches()
    {
        var compilation = CreateCompilation(
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using HotChocolate.Extension.CollectionEnhancements;

            public sealed class ServiceAttribute : Attribute { }
            public sealed class ParentAttribute : Attribute { }

            public abstract class AbstractThing;
            public struct PlainStruct(int Value);

            [CollectionEnhancementModel]
            public struct AttributedStruct
            {
                public int Value => 1;
            }

            [CollectionEnhancementModel]
            public sealed class NonQueryRoot
            {
                public SharedRoot Shared => new();
            }

            [CollectionEnhancementModel(IsQueryRoot = false)]
            public sealed class ExplicitFalseRoot
            {
                public int Id => 1;
            }

            [CollectionEnhancementModel(IsQueryRoot = true)]
            public sealed class SharedRoot
            {
                public string Title => "title";
                public IReadOnlyList<Row> GetRows([Service] object service) => Array.Empty<Row>();
                public string GetName() => "name";
                public int Unsupported(int skip) => skip;
            }

            public sealed class Row
            {
                public string Name => "row";
                public int[] Numbers => Array.Empty<int>();
                public IQueryable<Row> SelfQuery => Array.Empty<Row>().AsQueryable();
                public IReadOnlyList<Row> Children => Array.Empty<Row>();
                public Category Categories => new();
                public Category Category => new();
                public int? OptionalValue => 1;
                public Guid Id => Guid.Empty;
            }

            public sealed class Category
            {
                public IReadOnlyList<Coupon> Coupons => Array.Empty<Coupon>();
            }

            public sealed record Coupon(DateOnly PaymentDate, string Code);
            """);

        var attributeSymbol = compilation.GetTypeByMetadataName("HotChocolate.Extension.CollectionEnhancements.CollectionEnhancementModelAttribute");
        var sharedRoot = compilation.GetTypeByMetadataName("SharedRoot");
        var nonQueryRoot = compilation.GetTypeByMetadataName("NonQueryRoot");
        var rowType = compilation.GetTypeByMetadataName("Row");
        var abstractThing = compilation.GetTypeByMetadataName("AbstractThing");
        var plainStruct = compilation.GetTypeByMetadataName("PlainStruct");
        var attributedStruct = compilation.GetTypeByMetadataName("AttributedStruct");
        var explicitFalseRoot = compilation.GetTypeByMetadataName("ExplicitFalseRoot");
        Assert.NotNull(sharedRoot);
        Assert.NotNull(nonQueryRoot);
        Assert.NotNull(rowType);
        Assert.NotNull(abstractThing);
        Assert.NotNull(plainStruct);
        Assert.NotNull(attributedStruct);
        Assert.NotNull(explicitFalseRoot);

        var visitType = GetPrivateMethod(ModelDiscoveryType, "VisitType");
        var discoveredTypes = new Dictionary<INamedTypeSymbol, CollectionEnhancementSourceGenerator.ModelDiscovery.ObjectTypeInfo>(SymbolEqualityComparer.Default);
        visitType.Invoke(null, [sharedRoot, false, discoveredTypes]);
        visitType.Invoke(null, [sharedRoot, true, discoveredTypes]);
        Assert.True(discoveredTypes[sharedRoot].IsQueryRoot);
        Assert.Equal("Query", discoveredTypes[sharedRoot].GraphQlTypeName);

        Assert.True((bool)InvokePrivate(ModelDiscoveryType, "IsRoot", sharedRoot, attributeSymbol)!);
        Assert.True((bool)InvokePrivate(ModelDiscoveryType, "IsRoot", nonQueryRoot, attributeSymbol)!);
        Assert.False((bool)InvokePrivate(ModelDiscoveryType, "IsRoot", abstractThing, attributeSymbol)!);
        Assert.True((bool)InvokePrivate(ModelDiscoveryType, "IsRoot", attributedStruct, attributeSymbol)!);
        Assert.False((bool)InvokePrivate(ModelDiscoveryType, "IsRoot", plainStruct, attributeSymbol)!);

        Assert.True((bool)InvokePrivate(ModelDiscoveryType, "IsQueryRoot", sharedRoot, attributeSymbol)!);
        Assert.False((bool)InvokePrivate(ModelDiscoveryType, "IsQueryRoot", nonQueryRoot, attributeSymbol)!);
        Assert.False((bool)InvokePrivate(ModelDiscoveryType, "IsQueryRoot", explicitFalseRoot, attributeSymbol)!);
        Assert.False((bool)InvokePrivate(ModelDiscoveryType, "IsQueryRoot", rowType, attributeSymbol)!);

        var queryMembers = Assert.IsAssignableFrom<IEnumerable<ISymbol>>(InvokePrivate(ModelDiscoveryType, "GetQueryMembers", sharedRoot)!);
        Assert.Contains(queryMembers, member => member.Name == "GetRows");
        Assert.Contains(queryMembers, member => member.Name == "GetName");
        Assert.DoesNotContain(queryMembers, member => member.Name == "Unsupported");

        var rowMembers = Assert.IsAssignableFrom<IEnumerable<ISymbol>>(InvokePrivate(ModelDiscoveryType, "GetObjectMembers", rowType)!);
        Assert.Contains(rowMembers, member => member.Name == "Category");

        var childrenProperty = rowType.GetMembers().OfType<IPropertySymbol>().Single(member => member.Name == "Children");
        var numbersProperty = rowType.GetMembers().OfType<IPropertySymbol>().Single(member => member.Name == "Numbers");
        var selfQueryProperty = rowType.GetMembers().OfType<IPropertySymbol>().Single(member => member.Name == "SelfQuery");
        var nameProperty = rowType.GetMembers().OfType<IPropertySymbol>().Single(member => member.Name == "Name");
        var optionalValueProperty = rowType.GetMembers().OfType<IPropertySymbol>().Single(member => member.Name == "OptionalValue");
        var getRowsMethod = sharedRoot.GetMembers().OfType<IMethodSymbol>().Single(member => member.Name == "GetRows");

        var childrenArgs = new object?[] { childrenProperty.Type, null };
        Assert.True((bool)GetPrivateMethod(ModelDiscoveryType, "TryGetCollectionElementType").Invoke(null, childrenArgs)!);
        Assert.Equal("Row", ((ITypeSymbol)childrenArgs[1]!).Name);

        var numbersArgs = new object?[] { numbersProperty.Type, null };
        Assert.True((bool)GetPrivateMethod(ModelDiscoveryType, "TryGetCollectionElementType").Invoke(null, numbersArgs)!);
        Assert.Equal("Int32", ((ITypeSymbol)numbersArgs[1]!).Name);

        var queryArgs = new object?[] { selfQueryProperty.Type, null };
        Assert.True((bool)GetPrivateMethod(ModelDiscoveryType, "TryGetCollectionElementType").Invoke(null, queryArgs)!);

        var stringArgs = new object?[] { nameProperty.Type, null };
        Assert.False((bool)GetPrivateMethod(ModelDiscoveryType, "TryGetCollectionElementType").Invoke(null, stringArgs)!);
        Assert.Null(stringArgs[1]);

        var unsupportedArgs = new object?[] { optionalValueProperty.Type, null };
        Assert.False((bool)GetPrivateMethod(ModelDiscoveryType, "TryGetCollectionElementType").Invoke(null, unsupportedArgs)!);
        Assert.Null(unsupportedArgs[1]);

        Assert.True((bool)InvokePrivate(ModelDiscoveryType, "IsScalar", optionalValueProperty.Type)!);
        Assert.True((bool)InvokePrivate(ModelDiscoveryType, "IsScalar", compilation.GetSpecialType(SpecialType.System_String))!);
        Assert.False((bool)InvokePrivate(ModelDiscoveryType, "IsScalar", sharedRoot)!);
        Assert.True((bool)InvokePrivate(ModelDiscoveryType, "IsObjectType", rowType)!);
        Assert.False((bool)InvokePrivate(ModelDiscoveryType, "IsObjectType", compilation.GetSpecialType(SpecialType.System_Object))!);

        Assert.Equal("Rows", InvokePrivate(ModelDiscoveryType, "TrimGetPrefix", "Rows"));
        Assert.Equal(string.Empty, InvokePrivate(ModelDiscoveryType, "ToCamelCase", string.Empty));
        Assert.Equal("x", InvokePrivate(ModelDiscoveryType, "ToCamelCase", "X"));
        Assert.Equal("Value", InvokePrivate(ModelDiscoveryType, "ToPascalCase", "value"));
        Assert.Equal("X", InvokePrivate(ModelDiscoveryType, "ToPascalCase", "x"));
        Assert.Equal(string.Empty, InvokePrivate(ModelDiscoveryType, "ToPascalCase", string.Empty));
        Assert.Equal("sharedRoot", InvokePrivate(ModelDiscoveryType, "GetFieldName", sharedRoot));
        Assert.Equal("Value", InvokePrivate(SourceEmitterType, "ToPascalCase", "value"));
        Assert.Equal("X", InvokePrivate(SourceEmitterType, "ToPascalCase", "x"));
        Assert.Equal(string.Empty, InvokePrivate(SourceEmitterType, "ToPascalCase", string.Empty));
        Assert.Equal("y", InvokePrivate(ModelDiscoveryType, "Singularize", "ies"));
        Assert.Equal("class", InvokePrivate(ModelDiscoveryType, "Singularize", "classes"));
        Assert.Equal("coupon", InvokePrivate(ModelDiscoveryType, "Singularize", "coupons"));
        Assert.Equal("tag", InvokePrivate(ModelDiscoveryType, "Singularize", "tag"));

        Assert.Equal("rows", InvokePrivate(ModelDiscoveryType, "GetFieldName", getRowsMethod));
        Assert.Equal("name", InvokePrivate(ModelDiscoveryType, "GetFieldName", nameProperty));
        Assert.Equal("global::System.String", InvokePrivate(ModelDiscoveryType, "GetTypeName", nameProperty.Type));
        Assert.Equal("global::System.String?", InvokePrivate(ModelDiscoveryType, "GetFlatRowPropertyTypeName", nameProperty.Type));
        Assert.Equal("global::System.Guid", InvokePrivate(ModelDiscoveryType, "GetFlatRowPropertyTypeName", rowType.GetMembers().OfType<IPropertySymbol>().Single(member => member.Name == "Id").Type));
        Assert.Equal("int", ((ITypeSymbol)InvokePrivate(ModelDiscoveryType, "UnwrapNullable", optionalValueProperty.Type)!).ToDisplayString());

        var unsupportedMemberTypeError = Assert.Throws<TargetInvocationException>(() => InvokePrivate(ModelDiscoveryType, "GetMemberType", sharedRoot));
        Assert.IsType<NotSupportedException>(unsupportedMemberTypeError.InnerException);
        var unsupportedMemberKindError = Assert.Throws<TargetInvocationException>(() => InvokePrivate(ModelDiscoveryType, "GetMemberKind", sharedRoot));
        Assert.IsType<NotSupportedException>(unsupportedMemberKindError.InnerException);

        var ambiguousModel = CollectionEnhancementSourceGenerator.ModelDiscovery.Discover(compilation);
        var sharedObjectType = Assert.Single(
            ambiguousModel.ObjectTypes.Where(type => type.TypeName.Contains("SharedRoot", StringComparison.Ordinal)));
        var generatedCollection = Assert.Single(sharedObjectType.CollectionFields);
        Assert.Contains(generatedCollection.FlatPaths, path => path.Path == "categories.coupons");
        Assert.Contains(generatedCollection.FlatPaths, path => path.Path == "category.coupons");
        Assert.Contains(generatedCollection.FlatPaths, path => path.Path == "children");
        Assert.Contains(generatedCollection.FlatPaths, path => path.Path == "selfQuery");
        Assert.Contains(generatedCollection.FlatPaths, path => path.Prefix == "categoryCoupon");

        var tagPath = new CollectionEnhancementSourceGenerator.ModelDiscovery.FlatPathInfo(
            "items.tags",
            "itemTag",
            "global::Demo.Tag",
            "Tag",
            ImmutableArray.Create("Items", "Tags"));
        var notePath = new CollectionEnhancementSourceGenerator.ModelDiscovery.FlatPathInfo(
            "items.notes",
            "itemNote",
            "global::Demo.Note",
            "Note",
            ImmutableArray.Create("Items", "Notes"));
        var rowField = new CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo(
            "Id",
            CollectionEnhancementSourceGenerator.ModelDiscovery.MemberKindInfo.Property,
            "id",
            "global::System.Int32",
            "global::System.Int32");
        var tagField = new CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo(
            "Code",
            CollectionEnhancementSourceGenerator.ModelDiscovery.MemberKindInfo.Property,
            "code",
            "global::System.String",
            "global::System.String?");
        var noteField = new CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo(
            "Body",
            CollectionEnhancementSourceGenerator.ModelDiscovery.MemberKindInfo.Property,
            "body",
            "global::System.String",
            "global::System.String?");
        var rowsCollection = new CollectionEnhancementSourceGenerator.ModelDiscovery.CollectionFieldInfo(
            "GetRows",
            CollectionEnhancementSourceGenerator.ModelDiscovery.MemberKindInfo.Method,
            "rows",
            "global::System.Collections.Generic.IEnumerable<global::Demo.Row>",
            "global::Demo.Row",
            "Query",
            "Row",
            "QueryRowsGeneratedFlatRow",
            ImmutableArray.Create(tagPath, notePath));
        var moreRowsCollection = new CollectionEnhancementSourceGenerator.ModelDiscovery.CollectionFieldInfo(
            "GetMoreRows",
            CollectionEnhancementSourceGenerator.ModelDiscovery.MemberKindInfo.Method,
            "moreRows",
            "global::System.Collections.Generic.IEnumerable<global::Demo.Row>",
            "global::Demo.Row",
            "Query",
            "Row",
            "QueryMoreRowsGeneratedFlatRow",
            ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.FlatPathInfo>.Empty);
        var multiCollectionModel = new CollectionEnhancementSourceGenerator.ModelDiscovery.GenerationModel(
            ImmutableArray.Create(
                new CollectionEnhancementSourceGenerator.ModelDiscovery.ObjectTypeInfo(
                    "global::Demo.Query",
                    "Query",
                    true,
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo>.Empty,
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo>.Empty,
                    ImmutableArray.Create(rowsCollection, moreRowsCollection)),
                new CollectionEnhancementSourceGenerator.ModelDiscovery.ObjectTypeInfo(
                    "global::Demo.Row",
                    "Row",
                    false,
                    ImmutableArray.Create(rowField),
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo>.Empty,
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.CollectionFieldInfo>.Empty),
                new CollectionEnhancementSourceGenerator.ModelDiscovery.ObjectTypeInfo(
                    "global::Demo.Tag",
                    "Tag",
                    false,
                    ImmutableArray.Create(tagField),
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo>.Empty,
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.CollectionFieldInfo>.Empty),
                new CollectionEnhancementSourceGenerator.ModelDiscovery.ObjectTypeInfo(
                    "global::Demo.Note",
                    "Note",
                    false,
                    ImmutableArray.Create(noteField),
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.FieldInfo>.Empty,
                    ImmutableArray<CollectionEnhancementSourceGenerator.ModelDiscovery.CollectionFieldInfo>.Empty)));

        var emittedSource = CollectionEnhancementSourceGenerator.SourceEmitter.Emit(multiCollectionModel);
        Assert.Contains("QueryRowsGeneratedFlatRow", emittedSource, StringComparison.Ordinal);
        Assert.Contains("QueryMoreRowsGeneratedFlatRow", emittedSource, StringComparison.Ordinal);
        Assert.Contains("ItemTagCode", emittedSource, StringComparison.Ordinal);
        Assert.Contains("ItemNoteBody", emittedSource, StringComparison.Ordinal);

        var fallbackAttributeCompilation = CreateCompilation(
            """
            using System;

            namespace HotChocolate.Extension.CollectionEnhancements
            {
                public sealed class CollectionEnhancementModelAttribute : Attribute
                {
                    public string? IsQueryRoot { get; set; }
                }
            }

            [HotChocolate.Extension.CollectionEnhancements.CollectionEnhancementModel(IsQueryRoot = "yes")]
            public sealed class LocalRoot
            {
                public int Id => 1;
            }
            """);
        var fallbackAttributeSymbol = fallbackAttributeCompilation.GetTypeByMetadataName("HotChocolate.Extension.CollectionEnhancements.CollectionEnhancementModelAttribute");
        var localRoot = fallbackAttributeCompilation.GetTypeByMetadataName("LocalRoot");
        Assert.NotNull(localRoot);
        Assert.False(Assert.IsType<bool>(InvokePrivate(ModelDiscoveryType, "IsQueryRoot", localRoot, fallbackAttributeSymbol)));
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .GroupBy(static reference => reference.Display, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();

        return CSharpCompilation.Create(
            assemblyName: "GeneratorCoverage_" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture),
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static GeneratorDriver CreateDriver(CSharpCompilation compilation) =>
        CSharpGeneratorDriver.Create(
            generators:
            [
                new CollectionEnhancementSourceGenerator().AsSourceGenerator()
            ],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.Single().Options);

    private static Type ModelDiscoveryType =>
        typeof(CollectionEnhancementSourceGenerator).GetNestedType("ModelDiscovery", BindingFlags.NonPublic)!;

    private static Type SourceEmitterType =>
        typeof(CollectionEnhancementSourceGenerator).GetNestedType("SourceEmitter", BindingFlags.NonPublic)!;

    private static object? InvokePrivate(Type declaringType, string methodName, params object?[] arguments) =>
        GetPrivateMethod(declaringType, methodName).Invoke(null, arguments);

    private static MethodInfo GetPrivateMethod(Type declaringType, string methodName) =>
        declaringType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == methodName && method.GetParameters().Length > 0);
}
