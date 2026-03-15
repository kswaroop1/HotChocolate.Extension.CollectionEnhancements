using System.Collections;
using System.Globalization;
using System.Reflection;
using HotChocolate.Extension.CollectionEnhancements.Execution;
using HotChocolate.Extension.CollectionEnhancements.Metadata;
using HotChocolate.Extension.CollectionEnhancements.Schema;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestData;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;
using HotChocolate.Language;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

public sealed class MetadataUtilityCoverageTests
{
    [Fact]
    public void ComparisonHelper_ShouldHandleNullsNumbersDatesEnumsAndFallbacks()
    {
        Assert.True(ComparisonHelper.EqualsValue(null, null));
        Assert.True(ComparisonHelper.EqualsValue(12.5m, "12.5"));
        Assert.True(ComparisonHelper.EqualsValue(new DateOnly(2026, 3, 15), "2026-03-15"));
        Assert.True(ComparisonHelper.EqualsValue(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Unspecified), new DateOnly(2026, 3, 15)));
        Assert.True(ComparisonHelper.EqualsValue(OrderStatus.Active, "Active"));
        Assert.True(ComparisonHelper.EqualsValue(OrderStatus.Active, OrderStatus.Active));
        Assert.False(ComparisonHelper.EqualsValue(5, 6));

        Assert.Equal(0, ComparisonHelper.Compare(null, null));
        Assert.True(ComparisonHelper.Compare(null, 1) < 0);
        Assert.True(ComparisonHelper.Compare(1, null) > 0);
        Assert.True(ComparisonHelper.Compare(4, "3") > 0);
        Assert.True(ComparisonHelper.Compare(new DateOnly(2026, 3, 15), "2026-03-14") > 0);
        Assert.True(ComparisonHelper.Compare(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), "2026-03-16T00:00:00Z") < 0);
        Assert.True(ComparisonHelper.Compare("b", "a") > 0);
        Assert.True(ComparisonHelper.Compare(OrderStatus.Completed, OrderStatus.Active) > 0);
        Assert.True(ComparisonHelper.Compare(new NonComparable("b"), new NonComparable("a")) > 0);

        Assert.True(ComparisonHelper.TryConvertToDecimal((byte)1, out _));
        Assert.True(ComparisonHelper.TryConvertToDecimal((short)2, out _));
        Assert.True(ComparisonHelper.TryConvertToDecimal(3, out _));
        Assert.True(ComparisonHelper.TryConvertToDecimal(4L, out _));
        Assert.True(ComparisonHelper.TryConvertToDecimal(5f, out _));
        Assert.True(ComparisonHelper.TryConvertToDecimal(6d, out _));
        Assert.True(ComparisonHelper.TryConvertToDecimal("7.5", out var decimalValue));
        Assert.Equal(7.5m, decimalValue);
        Assert.False(ComparisonHelper.TryConvertToDecimal(Guid.NewGuid(), out _));
        Assert.False(ComparisonHelper.TryConvertToDecimal(null, out _));

        Assert.True(ComparisonHelper.TryConvertToDouble((byte)1, out _));
        Assert.True(ComparisonHelper.TryConvertToDouble((short)2, out _));
        Assert.True(ComparisonHelper.TryConvertToDouble(3, out _));
        Assert.True(ComparisonHelper.TryConvertToDouble(4L, out _));
        Assert.True(ComparisonHelper.TryConvertToDouble(5f, out _));
        Assert.True(ComparisonHelper.TryConvertToDouble(6d, out _));
        Assert.True(ComparisonHelper.TryConvertToDouble(7.5m, out _));
        Assert.True(ComparisonHelper.TryConvertToDouble("8.5", out var doubleValue));
        Assert.Equal(8.5d, doubleValue, precision: 3);
        Assert.False(ComparisonHelper.TryConvertToDouble(Guid.NewGuid(), out _));
        Assert.False(ComparisonHelper.TryConvertToDouble(null, out _));
    }

    [Fact]
    public void ComparisonHelper_ShouldCoverDateConversionAndComparableFallbacks()
    {
        Assert.True(ComparisonHelper.Compare(new ComparableValue(2), new ComparableValue(1)) > 0);
        Assert.ThrowsAny<Exception>(() => ComparisonHelper.Compare(new DateOnly(2026, 3, 15), "not-a-date"));
        Assert.ThrowsAny<Exception>(() => ComparisonHelper.Compare(new DateTime(2026, 3, 15), "not-a-date"));
        Assert.ThrowsAny<Exception>(() => ComparisonHelper.Compare("a", 1));
        Assert.ThrowsAny<Exception>(() => ComparisonHelper.Compare(OrderStatus.Active, 1));

        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(ComparisonHelper), "TryConvertToDateOnly", new DateTime(2026, 3, 15), null!)!);
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(ComparisonHelper), "TryConvertToDateOnly", "2026-03-15T00:00:00Z", null!)!);
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(typeof(ComparisonHelper), "TryConvertToDateOnly", 42, null!)!);
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(ComparisonHelper), "TryConvertToDateTime", new DateTime(2026, 3, 15), null!)!);
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(ComparisonHelper), "TryConvertToDateTime", new DateOnly(2026, 3, 15), null!)!);
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(ComparisonHelper), "TryConvertToDateTime", "2026-03-15", null!)!);
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(ComparisonHelper), "TryConvertToDateTime", "2026-03-15T00:00:00Z", null!)!);
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(typeof(ComparisonHelper), "TryConvertToDateTime", 42, null!)!);
    }

    [Fact]
    public void InputValueNormalizer_ShouldNormalizeNestedInputs_AndDetectEmptiness()
    {
        IDictionary mutable = new NullKeyDictionary
        {
            ["alpha"] = new ArrayList { 1, "two" },
            ["nested"] = new Hashtable { ["beta"] = null! },
            [null!] = "ignored"
        };

        var normalized = InputValueNormalizer.Normalize(mutable);
        var dictionary = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(normalized);
        var alpha = Assert.IsAssignableFrom<IReadOnlyList<object?>>(dictionary["alpha"]);
        var nested = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(dictionary["nested"]);

        Assert.Equal(2, alpha.Count);
        Assert.True(nested.ContainsKey("beta"));
        Assert.Empty(InputValueNormalizer.AsDictionary("scalar"));
        Assert.Empty(InputValueNormalizer.AsList("scalar"));
        Assert.True(InputValueNormalizer.IsEmpty(new Dictionary<string, object?> { ["x"] = null }));
        Assert.True(InputValueNormalizer.IsEmpty(new object?[] { null, Array.Empty<object?>() }));
        Assert.False(InputValueNormalizer.IsEmpty(new[] { 1 }));
    }

    [Fact]
    public void GraphQlNaming_ShouldHandleMethodsPropertiesFieldsAndPluralization()
    {
        Assert.Equal("securities", GraphQlNaming.GetFieldName(typeof(Query).GetMethod(nameof(Query.GetSecurities))!));
        Assert.Equal("name", GraphQlNaming.GetFieldName(typeof(Customer).GetProperty(nameof(Customer.Name))!));
        Assert.Equal("field", GraphQlNaming.GetFieldName(typeof(DummyHost).GetField(nameof(DummyHost.Field))!));
        Assert.Equal(string.Empty, GraphQlNaming.ToCamelCase(string.Empty));
        Assert.Equal("x", GraphQlNaming.ToCamelCase("X"));
        Assert.Equal("value", GraphQlNaming.ToCamelCase("Value"));
        Assert.Equal(string.Empty, GraphQlNaming.ToPascalCase(string.Empty));
        Assert.Equal("X", GraphQlNaming.ToPascalCase("x"));
        Assert.Equal("Value", GraphQlNaming.ToPascalCase("value"));
        Assert.Equal("category", GraphQlNaming.Singularize("categories"));
        Assert.Equal("class", GraphQlNaming.Singularize("classes"));
        Assert.Equal("coupon", GraphQlNaming.Singularize("coupons"));
        Assert.Equal("tag", GraphQlNaming.Singularize("tag"));
        Assert.Equal(nameof(Customer), GraphQlNaming.GetTypeName(typeof(Customer)));
    }

    [Fact]
    public void MemberAccessor_ShouldReadPropertyFieldAndMethod_AndRejectUnsupportedMembers()
    {
        var host = new DummyHost("sample");
        Assert.Equal("property-value", MemberAccessor.GetValue(typeof(DummyHost).GetProperty(nameof(DummyHost.Property))!, host));
        Assert.Equal("field-value", MemberAccessor.GetValue(typeof(DummyHost).GetField(nameof(DummyHost.Field))!, host));
        Assert.Equal("method-value", MemberAccessor.GetValue(typeof(DummyHost).GetMethod(nameof(DummyHost.Method))!, host));

        var changedEvent = typeof(DummyHost).GetEvent(nameof(DummyHost.Changed))!;
        var exception = Assert.Throws<TargetInvocationException>(() =>
            ReflectionTestSupport.InvokeStatic(typeof(MemberAccessor), "CreateGetter", changedEvent));
        Assert.IsType<NotSupportedException>(exception.InnerException);
    }

    [Fact]
    public void TypeInspection_And_GraphQlTypeReferenceHelper_ShouldClassifyTypes()
    {
        Assert.True(TypeInspection.IsNullable(typeof(string)));
        Assert.True(TypeInspection.IsNullable(typeof(int?)));
        Assert.Equal(typeof(int), TypeInspection.UnwrapNullable(typeof(int?)));
        Assert.True(TypeInspection.IsScalar(typeof(OrderStatus)));
        Assert.True(TypeInspection.IsScalar(typeof(Guid)));
        Assert.False(TypeInspection.IsScalar(typeof(SecurityDetails)));
        Assert.True(TypeInspection.IsNumeric(typeof(decimal?)));
        Assert.False(TypeInspection.IsNumeric(typeof(bool)));
        Assert.True(TypeInspection.IsStringLike(typeof(string)));
        Assert.False(TypeInspection.IsStringLike(typeof(Guid)));
        Assert.True(TypeInspection.IsCollectionType(typeof(Coupon[]), out var arrayElement));
        Assert.Equal(typeof(Coupon), arrayElement);
        Assert.True(TypeInspection.IsCollectionType(typeof(IQueryable<Customer>), out var queryableElement));
        Assert.Equal(typeof(Customer), queryableElement);
        Assert.True(TypeInspection.IsCollectionType(typeof(List<Order>), out var enumerableElement));
        Assert.Equal(typeof(Order), enumerableElement);
        Assert.False(TypeInspection.IsCollectionType(typeof(string), out var none));
        Assert.Null(none);
        Assert.False(TypeInspection.IsCollectionType(typeof(CultureInfo), out none));
        Assert.Null(none);

        Assert.Equal("Boolean", GraphQlTypeReferenceHelper.GetScalarTypeName(typeof(bool)));
        Assert.Equal("Int", GraphQlTypeReferenceHelper.GetScalarTypeName(typeof(long)));
        Assert.Equal("Float", GraphQlTypeReferenceHelper.GetScalarTypeName(typeof(double)));
        Assert.Equal("Decimal", GraphQlTypeReferenceHelper.GetScalarTypeName(typeof(decimal)));
        Assert.Equal("String", GraphQlTypeReferenceHelper.GetScalarTypeName(typeof(DateOnly)));
        Assert.Equal("String", GraphQlTypeReferenceHelper.GetScalarTypeName(typeof(Guid)));
        Assert.Equal(nameof(OrderStatus), GraphQlTypeReferenceHelper.GetScalarTypeName(typeof(OrderStatus)));
        Assert.Equal("Int!", GraphQlTypeReferenceHelper.GetRequiredScalarTypeSyntax(typeof(int)));
        Assert.Equal("Decimal", GraphQlTypeReferenceHelper.GetOptionalScalarTypeSyntax(typeof(decimal)));
        Assert.Equal($"Ce{nameof(OrderStatus)}OperationFilterInput", GraphQlTypeReferenceHelper.GetOperationFilterTypeName(typeof(OrderStatus)));
        Assert.Equal("CeFloatOperationFilterInput", GraphQlTypeReferenceHelper.GetOperationFilterTypeName(typeof(int)));
        Assert.Equal("CeBooleanOperationFilterInput", GraphQlTypeReferenceHelper.GetOperationFilterTypeName(typeof(bool)));
        Assert.Equal("CeStringOperationFilterInput", GraphQlTypeReferenceHelper.GetOperationFilterTypeName(typeof(string)));
        Assert.Equal("String", GraphQlTypeReferenceHelper.GetScalarTypeName(typeof(CultureInfo)));
        Assert.Equal("[String!]", GraphQlTypeReferenceHelper.Parse("[String!]"));
    }

    [Fact]
    public void ObjectTypeModel_And_CollectionSchemaCatalog_ShouldDiscoverExpectedShapes()
    {
        var catalog = CollectionSchemaCatalog.CreateDefault();
        Assert.Null(catalog.TryGetObjectType(typeof(string)));
        Assert.Null(catalog.TryGetObjectType(typeof(object)));
        Assert.Null(catalog.TryGetObjectType(typeof(AbstractCoverageType)));

        var customerModel = catalog.TryGetObjectType(typeof(Customer));
        Assert.NotNull(customerModel);
        Assert.NotNull(customerModel.FindScalar("name"));
        Assert.NotNull(customerModel.FindCollection("orders"));

        var queryModel = catalog.TryGetObjectType(typeof(Query));
        Assert.NotNull(queryModel);
        Assert.Equal("Query", queryModel.GraphQlTypeName);
        Assert.NotNull(queryModel.FindCollection("securities"));

        var safeGetTypes = ReflectionTestSupport.InvokeStatic(typeof(CollectionSchemaCatalog), "SafeGetTypes", new ThrowingAssembly());
        var recoveredTypes = Assert.IsAssignableFrom<IEnumerable<Type>>(safeGetTypes);
        Assert.Contains(typeof(string), recoveredTypes);

        var queryMembers = Assert.IsAssignableFrom<IEnumerable<MemberInfo>>(
            ReflectionTestSupport.InvokeStatic(typeof(CollectionSchemaCatalog), "GetQueryMembers", typeof(QueryCoverageType)));
        Assert.Contains(queryMembers, member => member.Name == nameof(QueryCoverageType.Supported));
        Assert.Contains(queryMembers, member => member.Name == nameof(QueryCoverageType.SupportedParent));
        Assert.DoesNotContain(queryMembers, member => member.Name == nameof(QueryCoverageType.Unsupported));
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionSchemaCatalog),
            "IsSupportedQueryParameter",
            typeof(QueryCoverageType).GetMethod(nameof(QueryCoverageType.SupportedParent))!.GetParameters().Single())!);

        Assert.True((bool)ReflectionTestSupport.InvokeStatic(typeof(CollectionSchemaCatalog), "IsApplicationAssembly", new NullNameAssembly())!);

        var eventInfo = typeof(DummyHost).GetEvent(nameof(DummyHost.Changed))!;
        var getMemberTypeFailure = Assert.Throws<TargetInvocationException>(() =>
            ReflectionTestSupport.InvokeStatic(typeof(CollectionSchemaCatalog), "GetMemberType", eventInfo));
        Assert.IsType<NotSupportedException>(getMemberTypeFailure.InnerException);
    }

    [Fact]
    public void FlatPathCatalog_And_FlatRowShapeCache_ShouldDeriveUniquePrefixes()
    {
        var catalog = CollectionSchemaCatalog.CreateDefault();
        var rootModel = catalog.TryGetObjectType(typeof(FlatCoverageRoot));
        Assert.NotNull(rootModel);
        var collectionField = rootModel.FindCollection("nestedRows");
        Assert.NotNull(collectionField);

        var paths = FlatPathCatalog.Discover(collectionField);
        Assert.Collection(
            paths,
            first => Assert.Equal("branchA.coupons", first.Path),
            second => Assert.Equal("branchB.coupons", second.Path));

        Assert.Equal("branchACoupon", paths[0].Prefix);
        Assert.Equal("branchBCoupon", paths[1].Prefix);

        var shape = FlatRowShapeCache.GetOrCreate(collectionField);
        Assert.Equal(paths.Count, shape.Paths.Count);
        Assert.Equal(paths[0].Path, shape.GeneratedFieldOwners["branchACouponPaymentDate"].Path);
        Assert.Equal(paths[1].Path, shape.GeneratedFieldOwners["branchBCouponPaymentDate"].Path);
    }

    [Fact]
    public void FlatPathCatalog_ShouldUseFallbackPrefix_WhenSingularizedSegmentsStayAmbiguous()
    {
        var catalog = CollectionSchemaCatalog.CreateDefault();
        var model = catalog.TryGetObjectType(typeof(FallbackPrefixRoot));
        Assert.NotNull(model);
        var collection = model.FindCollection("rows");
        Assert.NotNull(collection);

        var paths = FlatPathCatalog.Discover(collection);
        Assert.Equal(["categories.coupons", "category.coupons"], paths.Select(path => path.Path).ToArray());
        Assert.Equal(["categoryCoupon", "categoryCoupon"], paths.Select(path => path.Prefix).ToArray());
    }

    [Fact]
    public void CsvExportPlanBuilder_ShouldHandleOperationSelectionAndValidation()
    {
        Assert.Null(CsvExportPlanBuilder.TryCreate(document: null, operationName: null));

        var noDirective = Utf8GraphQLParser.Parse("""
            query {
              securities {
                id
              }
            }
            """);
        Assert.Null(CsvExportPlanBuilder.TryCreate(noDirective, operationName: null));

        var multipleRootFields = Utf8GraphQLParser.Parse("""
            query {
              left: securitiesFlat(expand: ["details.coupons"]) @export(format: CSV) { id }
              right: securitiesFlat(expand: ["details.coupons"]) { id }
            }
            """);
        Assert.Null(CsvExportPlanBuilder.TryCreate(multipleRootFields, operationName: null));

        var invalidSeparator = Utf8GraphQLParser.Parse("""
            query {
              securitiesFlat(expand: ["details.coupons"]) @export(format: CSV, separator: "||") { id }
            }
            """);
        Assert.Null(CsvExportPlanBuilder.TryCreate(invalidSeparator, operationName: null));

        var invalidFormat = Utf8GraphQLParser.Parse("""
            query {
              securitiesFlat(expand: ["details.coupons"]) @export(format: JSON) { id }
            }
            """);
        Assert.Null(CsvExportPlanBuilder.TryCreate(invalidFormat, operationName: null));

        var defaultFormat = Utf8GraphQLParser.Parse("""
            query {
              securitiesFlat(expand: ["details.coupons"]) @export { id }
            }
            """);
        Assert.NotNull(CsvExportPlanBuilder.TryCreate(defaultFormat, operationName: null));

        var noColumns = Utf8GraphQLParser.Parse("""
            query {
              securitiesFlat(expand: ["details.coupons"]) @export(format: CSV)
            }
            """);
        Assert.Null(CsvExportPlanBuilder.TryCreate(noColumns, operationName: null));

        var multiOperation = Utf8GraphQLParser.Parse("""
            query ExportCoupons {
              coupons: securitiesFlat(expand: ["details.coupons"]) @export(format: CSV, includeHeader: false, fileName: "rows.csv") {
                securityId: id
                coupon {
                  paymentDate: couponPaymentDate
                }
              }
            }

            mutation Ignored {
              ignored: __typename
            }
            """);

        var plan = CsvExportPlanBuilder.TryCreate(multiOperation, "ExportCoupons");
        Assert.NotNull(plan);
        Assert.Equal("coupons", plan.RootResponseName);
        Assert.Equal(",", plan.Separator);
        Assert.False(plan.IncludeHeader);
        Assert.Equal("rows.csv", plan.FileName);
        Assert.Collection(
            plan.Columns,
            first =>
            {
                Assert.Equal("securityId", first.Header);
                Assert.Equal(["securityId"], first.ResponsePath);
            },
            second =>
            {
                Assert.Equal("paymentDate", second.Header);
                Assert.Equal(["coupon", "paymentDate"], second.ResponsePath);
            });

        var queryFallback = Utf8GraphQLParser.Parse("""
            query {
              securitiesFlat(expand: ["details.coupons"]) @export(format: CSV) { id }
            }

            mutation IgnoreMe {
              ignored: __typename
            }
            """);
        Assert.NotNull(CsvExportPlanBuilder.TryCreate(queryFallback, operationName: null));
        Assert.Null(CsvExportPlanBuilder.TryCreate(queryFallback, operationName: "MissingOperation"));

        var fragmentOnly = Utf8GraphQLParser.Parse("""
            fragment CouponFields on Query {
              securitiesFlat(expand: ["details.coupons"]) {
                id
              }
            }
            """);
        Assert.Null(CsvExportPlanBuilder.TryCreate(fragmentOnly, operationName: null));
    }

    private sealed record NonComparable(string Value)
    {
        public override string ToString() => Value;
    }

    private sealed record ComparableValue(int Value) : IComparable
    {
        public int CompareTo(object? obj) => Value.CompareTo(((ComparableValue)obj!).Value);
    }

    public sealed record FlatCoverageCoupon(DateOnly PaymentDate);

    public sealed record FlatCoverageBranch(FlatCoverageCoupon[] Coupons);

    public sealed record FlatCoverageNested(FlatCoverageBranch BranchA, FlatCoverageBranch BranchB);

    public sealed class FlatCoverageRoot
    {
        public required FlatCoverageNested[] NestedRows { get; init; }
    }

    public sealed record FallbackCoverageCoupon(DateOnly PaymentDate);

    public sealed record FallbackCoverageLeaf(FallbackCoverageCoupon[] Coupons);

    public sealed record FallbackCoverageRow(FallbackCoverageLeaf Category, FallbackCoverageLeaf Categories);

    public sealed class FallbackPrefixRoot
    {
        public required FallbackCoverageRow[] Rows { get; init; }
    }

    private sealed class QueryCoverageType
    {
        public string Supported([Service] DummyService service) => service.Name;

        public string SupportedParent([Parent] DummyHost parent) => parent.Name;

        public int Unsupported(int limit) => limit;

        public string Property { get; init; } = string.Empty;
    }

    private sealed class NullKeyDictionary : IDictionary
    {
        private readonly List<DictionaryEntry> _entries = [];

        public object? this[object key]
        {
            get => _entries.Cast<DictionaryEntry>().First(entry => Equals(entry.Key, key)).Value;
            set
            {
                var index = _entries.FindIndex(entry => Equals(entry.Key, key));
                var dictionaryEntry = new DictionaryEntry(key, value);

                if (index >= 0)
                {
                    _entries[index] = dictionaryEntry;
                }
                else
                {
                    _entries.Add(dictionaryEntry);
                }
            }
        }

        public bool IsFixedSize => false;

        public bool IsReadOnly => false;

        public ICollection Keys => _entries.Select(entry => entry.Key).ToArray();

        public ICollection Values => _entries.Select(entry => entry.Value).ToArray();

        public int Count => _entries.Count;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public void Add(object key, object? value) => _entries.Add(new DictionaryEntry(key, value));

        public void Clear() => _entries.Clear();

        public bool Contains(object key) => _entries.Any(entry => Equals(entry.Key, key));

        public void CopyTo(Array array, int index) => _entries.ToArray().CopyTo(array, index);

        public IDictionaryEnumerator GetEnumerator() => new DictionaryEntryEnumerator(_entries.GetEnumerator());

        public void Remove(object key)
        {
            var index = _entries.FindIndex(entry => Equals(entry.Key, key));
            if (index >= 0)
            {
                _entries.RemoveAt(index);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class DictionaryEntryEnumerator(List<DictionaryEntry>.Enumerator enumerator) : IDictionaryEnumerator
        {
            private List<DictionaryEntry>.Enumerator _enumerator = enumerator;

            public object Current => Entry;

            public DictionaryEntry Entry => _enumerator.Current;

            public object Key => _enumerator.Current.Key;

            public object? Value => _enumerator.Current.Value;

            public bool MoveNext() => _enumerator.MoveNext();

            public void Reset() => throw new NotSupportedException();
        }
    }
}
