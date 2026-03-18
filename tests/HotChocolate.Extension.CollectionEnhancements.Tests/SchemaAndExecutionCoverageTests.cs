using System.Collections;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Serialization;
using HotChocolate.Execution;
using HotChocolate.Extension.CollectionEnhancements;
using HotChocolate.Extension.CollectionEnhancements.Execution;
using HotChocolate.Extension.CollectionEnhancements.Metadata;
using HotChocolate.Extension.CollectionEnhancements.Schema;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestData;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

public sealed class SchemaAndExecutionCoverageTests
{
    private static readonly CollectionSchemaCatalog Catalog = CollectionSchemaCatalog.CreateDefault();
    private static readonly CollectionExecutionEngine Engine = new(Catalog);

    [Fact]
    public async Task RequestInterceptor_ShouldReadPayloads_AndAttachExportPlan()
    {
        var interceptor = new CollectionEnhancementHttpRequestInterceptor();
        var services = TestServerFactory.CreateTestServices();
        var executor = await services.GetRequiredService<IRequestExecutorResolver>().GetRequestExecutorAsync();
        var exportQuery = """
            query ExportCoupons {
              securitiesFlat(expand: ["details.coupons"]) @export(format: CSV, fileName: "rows.csv") {
                id
              }
            }
            """;

        var getContext = new DefaultHttpContext { RequestServices = services };
        getContext.Request.QueryString = QueryString.Create(
            new Dictionary<string, string?>
            {
                ["query"] = exportQuery,
                ["operationName"] = "ExportCoupons"
            });

        await interceptor.OnCreateAsync(getContext, executor, OperationRequestBuilder.New(), CancellationToken.None);
        Assert.True(getContext.Items.ContainsKey(ExportContextDataKeys.CsvExportPlan));

        var readPayload = await ReflectionTestSupport.InvokeStaticAsync(
            typeof(CollectionEnhancementHttpRequestInterceptor),
            "ReadRequestPayloadAsync",
            getContext.Request,
            CancellationToken.None);
        Assert.Equal(exportQuery, readPayload!.GetType().GetProperty("Query")!.GetValue(readPayload));
        Assert.Equal("ExportCoupons", readPayload.GetType().GetProperty("OperationName")!.GetValue(readPayload));

        var queryOnlyContext = new DefaultHttpContext { RequestServices = services };
        queryOnlyContext.Request.QueryString = QueryString.Create(
            new Dictionary<string, string?>
            {
                ["query"] = exportQuery
            });
        var queryOnlyPayload = await ReflectionTestSupport.InvokeStaticAsync(
            typeof(CollectionEnhancementHttpRequestInterceptor),
            "ReadRequestPayloadAsync",
            queryOnlyContext.Request,
            CancellationToken.None);
        Assert.Null(queryOnlyPayload!.GetType().GetProperty("OperationName")!.GetValue(queryOnlyPayload));

        var postContext = new DefaultHttpContext { RequestServices = services };
        postContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"query":"query PostExport { securitiesFlat(expand:[\"details.coupons\"]) @export(format: CSV) { id } }","operationName":"PostExport"}"""));
        await interceptor.OnCreateAsync(postContext, executor, OperationRequestBuilder.New(), CancellationToken.None);
        Assert.True(postContext.Items.ContainsKey(ExportContextDataKeys.CsvExportPlan));
        Assert.Equal(0, postContext.Request.Body.Position);

        var invalidBodyContext = new DefaultHttpContext();
        invalidBodyContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{"));
        var invalidPayload = await ReflectionTestSupport.InvokeStaticAsync(
            typeof(CollectionEnhancementHttpRequestInterceptor),
            "ReadRequestPayloadAsync",
            invalidBodyContext.Request,
            CancellationToken.None);
        Assert.Null(invalidPayload!.GetType().GetProperty("Query")!.GetValue(invalidPayload));

        var unreadableContext = new DefaultHttpContext();
        unreadableContext.Request.Body = new UnreadableStream();
        var emptyPayload = await ReflectionTestSupport.InvokeStaticAsync(
            typeof(CollectionEnhancementHttpRequestInterceptor),
            "ReadRequestPayloadAsync",
            unreadableContext.Request,
            CancellationToken.None);
        Assert.Null(emptyPayload!.GetType().GetProperty("Query")!.GetValue(emptyPayload));

        var nullBodyContext = new DefaultHttpContext();
        nullBodyContext.Request.Body = null!;
        var nullBodyPayload = await ReflectionTestSupport.InvokeStaticAsync(
            typeof(CollectionEnhancementHttpRequestInterceptor),
            "ReadRequestPayloadAsync",
            nullBodyContext.Request,
            CancellationToken.None);
        Assert.Null(nullBodyPayload!.GetType().GetProperty("Query")!.GetValue(nullBodyPayload));

        var missingPropertiesContext = new DefaultHttpContext();
        missingPropertiesContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{}"""));
        var missingPropertiesPayload = await ReflectionTestSupport.InvokeStaticAsync(
            typeof(CollectionEnhancementHttpRequestInterceptor),
            "ReadRequestPayloadAsync",
            missingPropertiesContext.Request,
            CancellationToken.None);
        Assert.Null(missingPropertiesPayload!.GetType().GetProperty("Query")!.GetValue(missingPropertiesPayload));
        Assert.Null(missingPropertiesPayload.GetType().GetProperty("OperationName")!.GetValue(missingPropertiesPayload));

        var noExportPlan = await ReflectionTestSupport.InvokeStaticAsync(
            typeof(CollectionEnhancementHttpRequestInterceptor),
            "TryCreateExportPlanAsync",
            unreadableContext.Request,
            CancellationToken.None);
        Assert.Null(noExportPlan);
    }

    [Fact]
    public async Task ResponseFormatter_ShouldCoverFallbackAndCsvBranches()
    {
        var services = TestServerFactory.CreateTestServices();
        var executor = await services.GetRequiredService<IRequestExecutorResolver>().GetRequestExecutorAsync();
        var formatter = services.GetRequiredService<IHttpResponseFormatter>();

        formatter.CreateRequestFlags([]);

        var schemaContext = new DefaultHttpContext();
        schemaContext.Response.Body = new MemoryStream();
        await formatter.FormatAsync(schemaContext.Response, executor.Schema, 1, CancellationToken.None);

        var csvPlan = new CsvExportPlan(
            RootResponseName: "securitiesFlat",
            Separator: ";",
            IncludeHeader: true,
            FileName: "my\"rows.csv",
            Columns: [new CsvExportColumn("id", ["id"])]);

        var result = await executor.ExecuteQueryResultAsync("""
            query {
              securitiesFlat(expand: ["details.coupons"]) {
                id
              }
            }
            """);
        var csvContext = new DefaultHttpContext();
        csvContext.Response.Body = new MemoryStream();
        csvContext.Items[ExportContextDataKeys.CsvExportPlan] = csvPlan;

        await formatter.FormatAsync(csvContext.Response, result, [], HttpStatusCode.Accepted, CancellationToken.None);

        csvContext.Response.Body.Position = 0;
        using var reader = new StreamReader(csvContext.Response.Body, Encoding.UTF8);
        var csvBody = await reader.ReadToEndAsync();

        Assert.Equal("text/csv; charset=utf-8", csvContext.Response.ContentType);
        Assert.Equal((int)HttpStatusCode.Accepted, csvContext.Response.StatusCode);
        Assert.Equal("attachment; filename=\"myrows.csv\"", csvContext.Response.Headers.ContentDisposition.ToString());
        Assert.StartsWith("id", csvBody, StringComparison.Ordinal);
        Assert.Contains('\n', csvBody);

        var headerOnlyPlan = csvPlan with { RootResponseName = "missingRoot" };
        var headerOnly = (string)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionEnhancementHttpResponseFormatter),
            "BuildCsvPayload",
            CreateOperationResult("""{"data":{}}"""),
            headerOnlyPlan)!;
        Assert.Equal("id", headerOnly);

        var emptyPayload = (string)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionEnhancementHttpResponseFormatter),
            "BuildCsvPayload",
            CreateOperationResult("""{"errors":[{"message":"boom"}]}"""),
            headerOnlyPlan with { IncludeHeader = false })!;
        Assert.Equal(string.Empty, emptyPayload);

        var aggregateResult = await executor.ExecuteQueryResultAsync("""
            query {
              securitiesFlatAggregate(expand: ["details.coupons"]) {
                count
              }
            }
            """);
        var objectPayload = (string)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionEnhancementHttpResponseFormatter),
            "BuildCsvPayload",
            aggregateResult,
            new CsvExportPlan("securitiesFlatAggregate", ",", true, null, [new CsvExportColumn("count", ["count"])]))!;
        Assert.StartsWith("count\n", objectPayload, StringComparison.Ordinal);

        using var rowJson = JsonDocument.Parse("""[{"value":"a;b","quoted":"x\"y","flag":true},{"value":"line1\nline2","quoted":"plain","flag":false}]""");
        var rowPlan = new CsvExportPlan(
            "rows",
            ";",
            true,
            null,
            [
                new CsvExportColumn("value", ["value"]),
                new CsvExportColumn("quoted", ["quoted"]),
                new CsvExportColumn("missing", ["missing"]),
                new CsvExportColumn("flag", ["flag"])
            ]);
        var firstRow = (string)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionEnhancementHttpResponseFormatter),
            "BuildRow",
            rowJson.RootElement[0],
            rowPlan)!;
        Assert.Equal("\"a;b\";\"x\"\"y\";;true", firstRow);

        var secondRow = (string)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionEnhancementHttpResponseFormatter),
            "BuildRow",
            rowJson.RootElement[1],
            rowPlan)!;
        Assert.Equal("\"line1\nline2\";plain;;false", secondRow);

        var nestedMissingRow = (string)ReflectionTestSupport.InvokeStatic(
            typeof(CollectionEnhancementHttpResponseFormatter),
            "BuildRow",
            rowJson.RootElement[0],
            new CsvExportPlan(
                "rows",
                ",",
                true,
                null,
                [new CsvExportColumn("missingNested", ["value", "next"])]))!;
        Assert.Equal(string.Empty, nestedMissingRow);
    }

    [Fact]
    public async Task Registrar_PrivateHelpers_ShouldHandleErrorBranches_And_TaskUnwrapping()
    {
        var registrarType = typeof(CollectionEnhancementTypeRegistrar);

        var noHeaders = Assert.IsAssignableFrom<IEnumerable<string>>(
            ReflectionTestSupport.InvokeStatic(registrarType, "CollectLeafHeaders", new object?[] { null }));
        Assert.Empty(noHeaders);

        var nestedExportField = GetFieldNode("""
            query {
              nested: securitiesFlat(expand: ["details.coupons"]) @export(format: CSV) {
                id
              }
            }
            """);
        var nestedContext = ReflectionTestSupport.CreateResolverContext(nestedExportField, declaringTypeName: "Security");
        var rootQueryFailure = Assert.Throws<TargetInvocationException>(() =>
            ReflectionTestSupport.InvokeStatic(registrarType, "ValidateExportDirective", nestedContext, 1));
        var rootQueryError = Assert.IsType<GraphQLException>(rootQueryFailure.InnerException);
        Assert.Equal(CollectionEnhancementGraphQlErrors.ExportScopeInvalidCode, rootQueryError.Errors.Single().Code);
        Assert.Contains("root query fields", rootQueryError.Errors.Single().Message, StringComparison.Ordinal);

        var duplicateHeaderField = GetFieldNode("""
            query {
              securitiesFlat(expand: ["details.coupons"]) @export(format: CSV, separator: ",") {
                first: id
                first: isin
              }
            }
            """);
        var duplicateHeaderContext = ReflectionTestSupport.CreateResolverContext(duplicateHeaderField);
        var duplicateFailure = Assert.Throws<TargetInvocationException>(() =>
            ReflectionTestSupport.InvokeStatic(registrarType, "ValidateExportDirective", duplicateHeaderContext, 1));
        var duplicateHeaderError = Assert.IsType<GraphQLException>(duplicateFailure.InnerException);
        Assert.Equal(CollectionEnhancementGraphQlErrors.ExportDuplicateHeadersCode, duplicateHeaderError.Errors.Single().Code);
        Assert.Contains("duplicate headers", duplicateHeaderError.Errors.Single().Message, StringComparison.Ordinal);

        var invalidSeparatorField = GetFieldNode("""
            query {
              securitiesFlat(expand: ["details.coupons"]) @export(format: CSV, separator: "||") {
                id
              }
            }
            """);
        var invalidSeparatorContext = ReflectionTestSupport.CreateResolverContext(invalidSeparatorField);
        var separatorFailure = Assert.Throws<TargetInvocationException>(() =>
            ReflectionTestSupport.InvokeStatic(registrarType, "ValidateExportDirective", invalidSeparatorContext, 1));
        var separatorError = Assert.IsType<GraphQLException>(separatorFailure.InnerException);
        Assert.Equal(CollectionEnhancementGraphQlErrors.ExportSeparatorInvalidCode, separatorError.Errors.Single().Code);
        Assert.Contains("single character", separatorError.Errors.Single().Message, StringComparison.Ordinal);

        var defaultSeparatorField = GetFieldNode("""
            query {
              securitiesFlat(expand: ["details.coupons"]) @export {
                id
              }
            }
            """);
        var defaultSeparatorContext = ReflectionTestSupport.CreateResolverContext(defaultSeparatorField);
        ReflectionTestSupport.InvokeStatic(registrarType, "ValidateExportDirective", defaultSeparatorContext, 1);

        var unsupportedParentContext = ReflectionTestSupport.CreateResolverContext(GetFieldNode("""query { id }"""), parent: new object());
        var selectionFailure = Assert.Throws<TargetInvocationException>(() =>
            ReflectionTestSupport.InvokeStatic(registrarType, "GetSelection", unsupportedParentContext));
        var selectionError = Assert.IsType<GraphQLException>(selectionFailure.InnerException);
        Assert.Equal(CollectionEnhancementGraphQlErrors.UnexpectedAggregateParentContextCode, selectionError.Errors.Single().Code);
        Assert.Contains("Unexpected aggregate parent context", selectionError.Errors.Single().Message, StringComparison.Ordinal);

        var services = new ServiceCollection()
            .AddSingleton(new DummyService("svc"))
            .BuildServiceProvider();
        var cancellationToken = new CancellationTokenSource().Token;
        var resolverContext = ReflectionTestSupport.CreateResolverContext(
            GetFieldNode("""query { id }"""),
            parent: new DummyHost("source"),
            services: services,
            requestAborted: cancellationToken);

        var method = typeof(DummyHost).GetMethod(nameof(DummyHost.GetNumbers))!;
        var parameters = method.GetParameters();
        Assert.Equal(cancellationToken, ReflectionTestSupport.InvokeStaticGeneric(registrarType, "ResolveMethodArgument", [typeof(DummyHost)], resolverContext, new DummyHost("source"), parameters[2]));
        Assert.Equal("source", ((DummyHost)ReflectionTestSupport.InvokeStaticGeneric(registrarType, "ResolveMethodArgument", [typeof(DummyHost)], resolverContext, new DummyHost("source"), parameters[0])!).Name);
        Assert.Equal("svc", ((DummyService)ReflectionTestSupport.InvokeStaticGeneric(registrarType, "ResolveMethodArgument", [typeof(DummyHost)], resolverContext, new DummyHost("source"), parameters[1])!).Name);
        Assert.Equal(5, ReflectionTestSupport.InvokeStaticGeneric(registrarType, "ResolveMethodArgument", [typeof(DummyHost)], resolverContext, new DummyHost("source"), parameters[3]));
        Assert.Null(ReflectionTestSupport.InvokeStaticGeneric(
            registrarType,
            "ResolveMethodArgument",
            [typeof(DummyHost)],
            resolverContext,
            new DummyHost("source"),
            typeof(DummyHost).GetMethod(nameof(DummyHost.GetNumbersWithoutDefault))!.GetParameters().Single()));

        var parseOrder = Assert.IsAssignableFrom<IReadOnlyList<(string FieldName, bool Descending)>>(
            ReflectionTestSupport.InvokeStatic(
                registrarType,
                "ParseOrderArgument",
                new object?[]
                {
                    new object?[]
                    {
                        new Dictionary<string, object?> { ["id"] = null, ["isin"] = "DESC" }
                    }
                })!);
        Assert.Equal([("id", false), ("isin", true)], parseOrder.ToArray());

        var flatReferenceField = GetFieldNode("""
            query {
              securitiesFlatGroup(expand: ["details.coupons"], by: [null, "couponPaymentDate", " "]) {
                key {
                  couponPaymentDate
                }
              }
            }
            """);
        var flatReferenceContext = ReflectionTestSupport.CreateResolverContext(flatReferenceField);
        ReflectionTestSupport.InvokeStatic(
            registrarType,
            "ValidateFlatReferences",
            GetCollectionField<Query>("securities"),
            new[] { "details.coupons" },
            flatReferenceContext,
            true);

        var invalidFlatReferenceField = GetFieldNode("""
            query {
              securitiesFlat(expand: ["details.coupons"]) {
                callCallDate
              }
            }
            """);
        var invalidFlatReferenceContext = ReflectionTestSupport.CreateResolverContext(invalidFlatReferenceField);
        var invalidFlatReferenceFailure = Assert.Throws<TargetInvocationException>(() =>
            ReflectionTestSupport.InvokeStatic(
                registrarType,
                "ValidateFlatReferences",
                GetCollectionField<Query>("securities"),
                new[] { "details.coupons" },
                invalidFlatReferenceContext,
                false));
        var invalidFlatReferenceError = Assert.IsType<GraphQLException>(invalidFlatReferenceFailure.InnerException);
        Assert.Equal(CollectionEnhancementGraphQlErrors.FlatFieldSelectionInvalidCode, invalidFlatReferenceError.Errors.Single().Code);
        Assert.Contains("does not include generated fields", invalidFlatReferenceError.Errors.Single().Message, StringComparison.Ordinal);

        Assert.Null(await ReflectionTestSupport.InvokeStaticAsync(registrarType, "UnwrapTaskLikeAsync", new object?[] { null }));
        var unwrappedTask = Assert.IsAssignableFrom<IReadOnlyList<int>>(await ReflectionTestSupport.InvokeStaticAsync(registrarType, "UnwrapTaskLikeAsync", Task.FromResult<IReadOnlyList<int>>([3])));
        Assert.Equal(3, unwrappedTask.Single());
        Assert.Null(await ReflectionTestSupport.InvokeStaticAsync(registrarType, "UnwrapTaskLikeAsync", ValueTask.CompletedTask));
        var unwrappedValueTask = Assert.IsAssignableFrom<IReadOnlyList<int>>(await ReflectionTestSupport.InvokeStaticAsync(registrarType, "UnwrapTaskLikeAsync", ValueTask.FromResult<IReadOnlyList<int>>([4])));
        Assert.Equal(4, unwrappedValueTask.Single());
        Assert.Equal("plain", await ReflectionTestSupport.InvokeStaticAsync(registrarType, "UnwrapTaskLikeAsync", "plain"));

        var nonGenericTask = new Task(static () => { });
        nonGenericTask.Start();
        await nonGenericTask;
        Assert.Null(ReflectionTestSupport.InvokeStatic(registrarType, "GetTaskResult", nonGenericTask));
        Assert.Equal(9, ReflectionTestSupport.InvokeStatic(registrarType, "GetTaskResult", Task.FromResult(9)));

        var unsupportedMemberFailure = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await ReflectionTestSupport.InvokeStaticGenericAsync(
                registrarType,
                "ResolveCollectionSourceAsync",
                [typeof(DummyHost)],
                resolverContext,
                typeof(DummyHost).GetField(nameof(DummyHost.Field))!));
        Assert.Contains("Unsupported collection member", unsupportedMemberFailure.Message, StringComparison.Ordinal);

        var asyncResult = await ReflectionTestSupport.InvokeStaticGenericAsync(
            registrarType,
            "ResolveCollectionSourceAsync",
            [typeof(DummyHost)],
            resolverContext,
            typeof(DummyHost).GetMethod(nameof(DummyHost.GetNumbersAsync))!);
        Assert.Equal(3, ((IReadOnlyList<int>)asyncResult!).Single());

        var valueTaskResult = await ReflectionTestSupport.InvokeStaticGenericAsync(
            registrarType,
            "ResolveCollectionSourceAsync",
            [typeof(DummyHost)],
            resolverContext,
            typeof(DummyHost).GetMethod(nameof(DummyHost.GetNumbersValueTask))!);
        Assert.Equal(3, ((IReadOnlyList<int>)valueTaskResult!).Single());
    }

    [Fact]
    public void ExecutionEngine_ShouldCoverHelperBranches()
    {
        var securitiesField = GetCollectionField<Query>("securities");
        var customersField = GetCollectionField<Query>("customers");
        var peopleOrdersField = GetCollectionField<Person>("orders");

        Assert.Empty(Engine.CreateGroupRows(customersField, ExampleData.Customers, by: null, where: null, having: null, order: null, offset: null, limit: null, isFlat: false, expand: null));

        var nestedDetails = Engine.ResolveFieldValue(typeof(Security), ExampleData.Securities[0], "details", isFlat: false);
        Assert.IsType<SecurityDetails>(nestedDetails);
        Assert.Equal("USD", Engine.ResolveFieldValue(typeof(Security), ExampleData.Securities[0], "currency", isFlat: false));
        Assert.Null(Engine.ResolveFieldValue(typeof(Security), ExampleData.Securities[0], "missing", isFlat: false));
        Assert.Equal("x", Engine.ResolveFieldValue(typeof(Security), new Dictionary<string, object?> { ["field"] = "x" }, "field", isFlat: true));
        Assert.Null(Engine.ResolveFieldValue(typeof(Security), new Dictionary<string, object?>(), "field", isFlat: true));

        Assert.True(Engine.ValidateNumericAggregateField(securitiesField, "price", isFlat: false));
        Assert.False(Engine.ValidateNumericAggregateField(securitiesField, "currency", isFlat: false));
        Assert.True(Engine.ValidateNumericAggregateField(securitiesField, "couponInterestRate", isFlat: true));
        Assert.False(Engine.ValidateNumericAggregateField(securitiesField, "couponPaymentDate", isFlat: true));
        Assert.True(Engine.ValidateStringAggregateField(customersField, "name", isFlat: false));
        Assert.False(Engine.ValidateStringAggregateField(customersField, "id", isFlat: false));
        Assert.True(Engine.ValidateStringAggregateField(securitiesField, "underlyingRic", isFlat: true));
        Assert.False(Engine.ValidateStringAggregateField(securitiesField, "couponInterestRate", isFlat: true));

        var filteredCustomers = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?>
            {
                ["and"] = new object?[]
                {
                    new Dictionary<string, object?> { ["name"] = new Dictionary<string, object?> { ["neq"] = "Cara Holdings" } },
                    new Dictionary<string, object?> { ["ordersAggregate"] = new Dictionary<string, object?>() },
                    new Dictionary<string, object?> { ["ordersGroup"] = new Dictionary<string, object?>() },
                    new Dictionary<string, object?> { ["or"] = new object?[]
                        {
                            new Dictionary<string, object?> { ["id"] = new Dictionary<string, object?> { ["eq"] = 1L } },
                            new Dictionary<string, object?> { ["id"] = new Dictionary<string, object?> { ["eq"] = 2 } }
                        }
                    },
                    new Dictionary<string, object?> { ["not"] = new Dictionary<string, object?> { ["id"] = new Dictionary<string, object?> { ["eq"] = 2 } } }
                }
            },
            order: new object?[] { new Dictionary<string, object?> { ["id"] = "DESC" } },
            offset: 0,
            limit: 10);
        Assert.Single(filteredCustomers);

        var aggregateFilteredCustomers = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?>
            {
                ["ordersAggregate"] = new Dictionary<string, object?>
                {
                    ["where"] = new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "Active" } },
                    ["having"] = new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gte"] = 3 } }
                }
            },
            order: null,
            offset: null,
            limit: null);
        Assert.Single(aggregateFilteredCustomers);

        var groupedCustomers = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?>
            {
                ["ordersGroup"] = new Dictionary<string, object?>
                {
                    ["by"] = new object?[] { "status" },
                    ["having"] = new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gte"] = 2 } },
                    ["offset"] = 0L,
                    ["limit"] = "10"
                }
            },
            order: null,
            offset: null,
            limit: null);
        Assert.Single(groupedCustomers);

        var impossibleGroupCustomers = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?>
            {
                ["ordersGroup"] = new Dictionary<string, object?>
                {
                    ["by"] = new object?[] { "status" },
                    ["having"] = new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gt"] = 100 } }
                }
            },
            order: null,
            offset: null,
            limit: null);
        Assert.Empty(impossibleGroupCustomers);

        var flatRows = Engine.ApplyFlatArguments(
            securitiesField,
            ExampleData.Securities,
            expand: ["details.coupons"],
            where: new Dictionary<string, object?>
            {
                ["and"] = new object?[]
                {
                    new Dictionary<string, object?> { ["couponInterestRate"] = new Dictionary<string, object?> { ["gt"] = 0.02 } },
                    new Dictionary<string, object?> { ["or"] = new object?[]
                        {
                            new Dictionary<string, object?> { ["currency"] = new Dictionary<string, object?> { ["eq"] = "USD" } },
                            new Dictionary<string, object?> { ["currency"] = new Dictionary<string, object?> { ["eq"] = "EUR" } }
                        }
                    },
                    new Dictionary<string, object?> { ["not"] = new Dictionary<string, object?> { ["couponPaymentDate"] = new Dictionary<string, object?> { ["lt"] = "2023-05-01" } } }
                }
            },
            order: new object?[]
            {
                new Dictionary<string, object?>(),
                new Dictionary<string, object?> { ["couponPaymentDate"] = null },
                new Dictionary<string, object?> { ["couponPaymentDate"] = "DESC" }
            },
            offset: 1,
            limit: 2);
        Assert.Equal(2, flatRows.Count);

        var flatSelection = new AggregateSelectionContext(securitiesField, isFlat: true, flatRows);
        Assert.Equal("Flat", flatSelection.ResultPrefix);
        Assert.Equal(2, Engine.ResolveCount(flatSelection, new Dictionary<string, object?> { ["couponInterestRate"] = new Dictionary<string, object?> { ["gt"] = 0.03 } }));

        var objectSelection = new AggregateSelectionContext(peopleOrdersField, isFlat: false, ExampleData.People[0].Orders.Cast<object>().ToArray());
        Assert.Equal(string.Empty, objectSelection.ResultPrefix);
        Assert.Equal(1, Engine.ResolveCount(objectSelection, new Dictionary<string, object?> { ["reference"] = new Dictionary<string, object?> { ["eq"] = "P-BETA" } }));

        var projection = new AggregateProjection(objectSelection, AggregateOperator.StringAggDistinct, "-", [("reference", true)]);
        Assert.Equal("P-GAMMA-P-DELTA-P-BETA-P-ALPHA", Engine.ResolveAggregateProjectionField(projection, "reference"));
        Assert.Equal("P-GAMMA", Engine.ResolveAggregateProjectionField(new AggregateProjection(objectSelection, AggregateOperator.Max, null, null), "reference"));
        Assert.Null(Engine.ResolveAggregateProjectionField(new AggregateProjection(objectSelection, (AggregateOperator)999, null, null), "reference"));

        var havingSelection = Engine.CreateAggregateSelection(
            peopleOrdersField,
            ExampleData.People[0].Orders,
            where: new Dictionary<string, object?> { ["total"] = new Dictionary<string, object?> { ["gte"] = 80 } },
            having: new Dictionary<string, object?>
            {
                ["and"] = new object?[]
                {
                    new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gte"] = 3 } },
                    new Dictionary<string, object?> { ["or"] = new object?[]
                        {
                            new Dictionary<string, object?> { ["sum"] = new Dictionary<string, object?> { ["total"] = new Dictionary<string, object?> { ["gte"] = 1000 } } },
                            new Dictionary<string, object?> { ["not"] = new Dictionary<string, object?> { ["avg"] = new Dictionary<string, object?> { ["total"] = new Dictionary<string, object?> { ["lt"] = 100 } } } }
                        }
                    }
                }
            },
            isFlat: false,
            expand: null);
        Assert.NotNull(havingSelection);

        var emptyOperationsSelection = Engine.CreateAggregateSelection(
            peopleOrdersField,
            ExampleData.People[0].Orders,
            where: null,
            having: new Dictionary<string, object?>
            {
                ["sum"] = new Dictionary<string, object?> { ["total"] = new Dictionary<string, object?>() },
                ["count"] = new Dictionary<string, object?>()
            },
            isFlat: false,
            expand: null);
        Assert.NotNull(emptyOperationsSelection);

        Assert.Null(Engine.CreateAggregateSelection(
            peopleOrdersField,
            ExampleData.People[1].Orders,
            where: null,
            having: new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gt"] = 10 } },
            isFlat: false,
            expand: null));

        var grouped = Engine.CreateGroupRows(
            peopleOrdersField,
            ExampleData.People[0].Orders,
            by: new object?[] { "status" },
            where: null,
            having: new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gte"] = 1 } },
            order:
            new object?[]
            {
                new Dictionary<string, object?>(),
                new Dictionary<string, object?> { ["key"] = new Dictionary<string, object?> { ["status"] = null } },
                new Dictionary<string, object?> { ["key"] = new Dictionary<string, object?> { ["status"] = "ASC" } },
                new Dictionary<string, object?> { ["count"] = "DESC" },
                new Dictionary<string, object?> { ["sum"] = new Dictionary<string, object?> { ["total"] = "DESC" } }
            },
            offset: 1,
            limit: 2,
            isFlat: false,
            expand: null);
        Assert.Equal(2, grouped.Count);

        var noLimitGroups = Engine.CreateGroupRows(
            peopleOrdersField,
            ExampleData.People[0].Orders,
            by: new object?[] { "status" },
            where: null,
            having: null,
            order: null,
            offset: 1,
            limit: null,
            isFlat: false,
            expand: null);
        Assert.True(noLimitGroups.Count >= 1);

        var flatAggregate = Engine.CreateAggregateSelection(
            securitiesField,
            ExampleData.Securities,
            where: new Dictionary<string, object?> { ["couponInterestRate"] = new Dictionary<string, object?> { ["gte"] = 0.02 } },
            having: new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gte"] = 5 } },
            isFlat: true,
            expand: ["details.coupons"]);
        Assert.NotNull(flatAggregate);

        var invalidFlatField = Assert.Throws<InvalidOperationException>(() => Engine.ApplyFlatArguments(
            securitiesField,
            ExampleData.Securities,
            expand: ["details.coupons"],
            where: new Dictionary<string, object?> { ["callCallDate"] = new Dictionary<string, object?> { ["gt"] = "2024-01-01" } },
            order: null,
            offset: null,
            limit: null));
        Assert.Contains("does not include generated fields", invalidFlatField.Message, StringComparison.Ordinal);

        var invalidExpand = Assert.Throws<InvalidOperationException>(() => Engine.ApplyFlatArguments(
            securitiesField,
            ExampleData.Securities,
            expand: ["details.missing"],
            where: null,
            order: null,
            offset: null,
            limit: null));
        Assert.Contains("invalid", invalidExpand.Message, StringComparison.OrdinalIgnoreCase);

        var customModel = Catalog.TryGetObjectType(typeof(NullExpandQuery));
        Assert.NotNull(customModel);
        var customField = customModel.FindCollection("nullableRows");
        Assert.NotNull(customField);
        var expandedWithNulls = Engine.ApplyFlatArguments(
            customField,
            new NullExpandQuery().NullableRows,
            expand: ["maybe.rows"],
            where: null,
            order: null,
            offset: null,
            limit: null);
        Assert.Single(expandedWithNulls);

        var flatSelectionWithDuplicates = new AggregateSelectionContext(
            securitiesField,
            isFlat: true,
            [
                new Dictionary<string, object?> { ["reference"] = "A" },
                new Dictionary<string, object?> { ["reference"] = "A" },
                new Dictionary<string, object?> { ["reference"] = "B" }
            ]);
        Assert.Equal(2, Engine.ResolveAggregateProjectionField(new AggregateProjection(flatSelectionWithDuplicates, AggregateOperator.CountDistinct, null, null), "reference"));

        Assert.Null(Engine.ResolveAggregateProjectionField(new AggregateProjection(objectSelection, AggregateOperator.Sum, null, null), "missing"));

        var collectReferencedFields = Assert.IsAssignableFrom<IEnumerable<string>>(
            ReflectionTestSupport.InvokeStatic(
                typeof(CollectionExecutionEngine),
                "CollectReferencedFields",
                new Dictionary<string, object?>
                {
                    ["and"] = new object?[]
                    {
                        new Dictionary<string, object?> { ["id"] = new Dictionary<string, object?>() },
                        new Dictionary<string, object?> { ["or"] = new object?[] { new Dictionary<string, object?> { ["name"] = new Dictionary<string, object?> { ["eq"] = "x" } } } },
                        new Dictionary<string, object?> { ["not"] = new Dictionary<string, object?> { ["currency"] = new Dictionary<string, object?> { ["eq"] = "USD" } } }
                    }
                }));
        Assert.Equal(["name", "currency"], collectReferencedFields.ToArray());

        Assert.Equal(42, ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ConvertToNullableInt", "42"));
        Assert.Equal(5, ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ConvertToNullableInt", 5));
        Assert.Equal(7, ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ConvertToNullableInt", 7L));
        Assert.Null(ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ConvertToNullableInt", new object?[] { null }));
        Assert.Equal(AggregateOperator.Var, ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ParseOperator", "var"));
        Assert.Equal(AggregateOperator.Varp, ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ParseOperator", "varp"));
        Assert.Equal(AggregateOperator.StringAggDistinct, ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ParseOperator", "stringAggDistinct"));
        var invalidOperator = Assert.Throws<TargetInvocationException>(() => ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ParseOperator", "bogus"));
        Assert.Contains("Unsupported aggregate operator", invalidOperator.InnerException!.Message, StringComparison.Ordinal);

        var momentsType = typeof(CollectionExecutionEngine).GetNestedType("RunningMoments", BindingFlags.NonPublic)!;
        var defaultMoments = ReflectionTestSupport.InvokeStatic(momentsType, "Calculate", Array.Empty<double>());
        Assert.Equal(0d, (double)momentsType.GetProperty("SampleVariance")!.GetValue(defaultMoments)!);
        Assert.Equal(0d, (double)momentsType.GetProperty("PopulationVariance")!.GetValue(defaultMoments)!);
        Assert.Equal(0d, (double)momentsType.GetProperty("SampleStandardDeviation")!.GetValue(defaultMoments)!);
        var zeroVarianceMoments = ReflectionTestSupport.InvokeStatic(momentsType, "Calculate", new double[] { 5, 5, 5 });
        Assert.Equal(0d, (double)momentsType.GetProperty("Skewness")!.GetValue(zeroVarianceMoments)!);
        Assert.Equal(0d, (double)momentsType.GetProperty("Kurtosis")!.GetValue(zeroVarianceMoments)!);

        var projectionEqualityComparer = ReflectionTestSupport.GetNestedPrivateType<IEqualityComparer<object?>>(
            typeof(CollectionExecutionEngine),
            "ProjectionEqualityComparer",
            "Instance");
        Assert.True(projectionEqualityComparer.Equals(5, 5m));

        var groupKeyComparer = ReflectionTestSupport.GetNestedPrivateType<IEqualityComparer<IReadOnlyDictionary<string, object?>>>(
            typeof(CollectionExecutionEngine),
            "GroupKeyComparer",
            "Instance");
        Assert.False(groupKeyComparer.Equals(null, new Dictionary<string, object?>()));
        Assert.True(groupKeyComparer.Equals(
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "A" },
            new Dictionary<string, object?> { ["name"] = "A", ["id"] = 1L }));
        Assert.NotEqual(
            groupKeyComparer.GetHashCode(new Dictionary<string, object?> { ["id"] = 1 }),
            groupKeyComparer.GetHashCode(new Dictionary<string, object?> { ["id"] = 2 }));
    }

    [Fact]
    public void ExecutionEngine_ResidualBranches_ShouldBeCovered()
    {
        var securitiesField = GetCollectionField<Query>("securities");
        var customersField = GetCollectionField<Query>("customers");
        var peopleOrdersField = GetCollectionField<Person>("orders");
        var unknownField = new CollectionFieldModel(
            typeof(UnknownCollectionHost).GetProperty(nameof(UnknownCollectionHost.Values))!,
            "values",
            typeof(Guid[]),
            typeof(Guid),
            nameof(UnknownCollectionHost),
            "Guid");

        Assert.True(Engine.ValidateNumericAggregateField(securitiesField, "price", isFlat: true));
        Assert.True(Engine.ValidateStringAggregateField(securitiesField, "currency", isFlat: true));
        Assert.False(Engine.ValidateNumericAggregateField(unknownField, "value", isFlat: false));
        Assert.False(Engine.ValidateStringAggregateField(unknownField, "value", isFlat: false));
        Assert.False(Engine.ValidateNumericAggregateField(unknownField, "value", isFlat: true));
        Assert.False(Engine.ValidateStringAggregateField(unknownField, "value", isFlat: true));

        Assert.Throws<InvalidOperationException>(() => Engine.ResolveFieldValue(typeof(Guid), Guid.NewGuid(), "value", isFlat: false));
        Assert.Throws<InvalidOperationException>(() => Engine.ApplyCollectionArguments(
            unknownField,
            new[] { Guid.NewGuid() },
            new Dictionary<string, object?> { ["value"] = new Dictionary<string, object?> { ["eq"] = Guid.NewGuid().ToString() } },
            order: null,
            offset: null,
            limit: null));

        var baseFlatSelection = Engine.CreateAggregateSelection(
            securitiesField,
            ExampleData.Securities,
            where: null,
            having: null,
            isFlat: true,
            expand: null);
        Assert.NotNull(baseFlatSelection);
        Assert.Equal(ExampleData.Securities.Count, baseFlatSelection.Rows.Count);

        var baseFlatGroups = Engine.CreateGroupRows(
            securitiesField,
            ExampleData.Securities,
            by: new object?[] { null, " ", "currency" },
            where: null,
            having: null,
            order: new object?[] { new Dictionary<string, object?> { ["key"] = new Dictionary<string, object?> { ["currency"] = "ASC" } } },
            offset: null,
            limit: null,
            isFlat: true,
            expand: null);
        Assert.NotEmpty(baseFlatGroups);

        var multiSortedCustomers = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            where: null,
            order: new object?[]
            {
                new Dictionary<string, object?> { ["name"] = "ASC" },
                new Dictionary<string, object?> { ["id"] = "DESC" }
            },
            offset: null,
            limit: null);
        Assert.Equal(ExampleData.Customers.Count, multiSortedCustomers.Count);

        var multiSortedCustomersAscending = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            where: null,
            order: new object?[]
            {
                new Dictionary<string, object?> { ["name"] = "ASC" },
                new Dictionary<string, object?> { ["id"] = "ASC" }
            },
            offset: null,
            limit: null);
        Assert.Equal(ExampleData.Customers.Count, multiSortedCustomersAscending.Count);

        var unknownFlatFieldRows = Engine.ApplyFlatArguments(
            securitiesField,
            ExampleData.Securities,
            expand: ["details.coupons"],
            where: new Dictionary<string, object?> { ["madeUpField"] = new Dictionary<string, object?> { ["eq"] = 1 } },
            order: null,
            offset: null,
            limit: null);
        Assert.Empty(unknownFlatFieldRows);

        var skippedFlatFilterRows = Engine.ApplyFlatArguments(
            securitiesField,
            ExampleData.Securities,
            expand: ["details.coupons"],
            where: new Dictionary<string, object?> { ["couponInterestRate"] = new Dictionary<string, object?>() },
            order: null,
            offset: null,
            limit: null);
        Assert.NotEmpty(skippedFlatFilterRows);

        var scalarOperationsType = typeof(CollectionExecutionEngine);
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(scalarOperationsType, "MatchesScalarOperations", 5, new Dictionary<string, object?> { ["neq"] = 4 })!);
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(scalarOperationsType, "MatchesScalarOperations", 5, new Dictionary<string, object?> { ["in"] = new object?[] { 4, 5 } })!);
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(scalarOperationsType, "MatchesScalarOperations", 5, new Dictionary<string, object?> { ["in"] = new object?[] { 1, 2 } })!);
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(scalarOperationsType, "MatchesScalarOperations", 5, new Dictionary<string, object?> { ["nin"] = new object?[] { 1, 2 } })!);
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(scalarOperationsType, "MatchesScalarOperations", 5, new Dictionary<string, object?> { ["lte"] = 4 })!);
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(scalarOperationsType, "MatchesScalarOperations", 5, new Dictionary<string, object?> { ["nin"] = new object?[] { 4, 5 } })!);
        Assert.True((bool)ReflectionTestSupport.InvokeStatic(scalarOperationsType, "MatchesScalarOperations", 5, new Dictionary<string, object?> { ["eq"] = new Dictionary<string, object?>() })!);

        var noMatchOrCustomers = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?>
            {
                ["or"] = new object?[]
                {
                    new Dictionary<string, object?> { ["id"] = new Dictionary<string, object?> { ["eq"] = 99 } },
                    new Dictionary<string, object?> { ["id"] = new Dictionary<string, object?> { ["eq"] = 98 } }
                }
            },
            order: null,
            offset: null,
            limit: null);
        Assert.Empty(noMatchOrCustomers);

        var aggregateCriteriaIgnored = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?> { ["ordersAggregate"] = 5 },
            order: null,
            offset: null,
            limit: null);
        Assert.Equal(ExampleData.Customers.Count, aggregateCriteriaIgnored.Count);

        var groupCriteriaIgnored = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?> { ["ordersGroup"] = 5 },
            order: null,
            offset: null,
            limit: null);
        Assert.Equal(ExampleData.Customers.Count, groupCriteriaIgnored.Count);

        var aggregateWithHavingOnly = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?>
            {
                ["ordersAggregate"] = new Dictionary<string, object?>
                {
                    ["having"] = new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gte"] = 1 } }
                }
            },
            order: null,
            offset: null,
            limit: null);
        Assert.NotEmpty(aggregateWithHavingOnly);

        var aggregateWithWhereOnly = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?>
            {
                ["ordersAggregate"] = new Dictionary<string, object?>
                {
                    ["where"] = new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "Active" } }
                }
            },
            order: null,
            offset: null,
            limit: null);
        Assert.NotEmpty(aggregateWithWhereOnly);

        var groupWithSparseCriteria = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?>
            {
                ["ordersGroup"] = new Dictionary<string, object?>
                {
                    ["by"] = new object?[] { "status" }
                }
            },
            order: null,
            offset: null,
            limit: null);
        Assert.NotEmpty(groupWithSparseCriteria);

        var groupWithFilterAndOrder = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?>
            {
                ["ordersGroup"] = new Dictionary<string, object?>
                {
                    ["by"] = new object?[] { "status" },
                    ["where"] = new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "Active" } },
                    ["order"] = new object?[] { new Dictionary<string, object?> { ["count"] = "DESC" } }
                }
            },
            order: null,
            offset: null,
            limit: null);
        Assert.NotEmpty(groupWithFilterAndOrder);

        var groupWithoutBy = Engine.ApplyCollectionArguments(
            customersField,
            ExampleData.Customers,
            new Dictionary<string, object?>
            {
                ["ordersGroup"] = new Dictionary<string, object?>
                {
                    ["order"] = new object?[] { new Dictionary<string, object?> { ["count"] = "DESC" } }
                }
            },
            order: null,
            offset: null,
            limit: null);
        Assert.Empty(groupWithoutBy);

        Assert.Empty((IReadOnlyList<object>)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ToObjectRows", new object?[] { null })!);
        Assert.Equal(2, ((IReadOnlyList<object>)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ToObjectRows", new object?[] { ExampleData.Securities.Take(2).Cast<object>().ToArray() })!).Count);
        Assert.Equal(2, ((IReadOnlyList<object>)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ToObjectRows", new ArrayList { 1, 2 })!).Count);
        Assert.Single((IReadOnlyList<object>)ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ToObjectRows", 42)!);

        Assert.Null(Engine.CreateAggregateSelection(
            peopleOrdersField,
            ExampleData.People[0].Orders,
            where: null,
            having: new Dictionary<string, object?>
            {
                ["or"] = new object?[]
                {
                    new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gt"] = 20 } },
                    new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["lt"] = 0 } }
                }
            },
            isFlat: false,
            expand: null));

        Assert.Null(Engine.CreateAggregateSelection(
            peopleOrdersField,
            ExampleData.People[0].Orders,
            where: null,
            having: new Dictionary<string, object?> { ["not"] = new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["lt"] = 10 } } },
            isFlat: false,
            expand: null));

        Assert.NotNull(Engine.CreateAggregateSelection(
            peopleOrdersField,
            ExampleData.People[0].Orders,
            where: null,
            having: new Dictionary<string, object?> { ["sum"] = new Dictionary<string, object?> { ["total"] = new Dictionary<string, object?>() } },
            isFlat: false,
            expand: null));

        var numericAggregateType = typeof(CollectionExecutionEngine).GetNestedType("NumericAggregate", BindingFlags.NonPublic)!;
        Assert.Null(ReflectionTestSupport.InvokeInstance(
            Engine,
            "GetNumericAggregate",
            new AggregateSelectionContext(peopleOrdersField, isFlat: false, ExampleData.People[0].Orders.Cast<object>().ToArray()),
            "total",
            Enum.ToObject(numericAggregateType, 999)));

        var flatTextSelection = new AggregateSelectionContext(
            securitiesField,
            isFlat: true,
            [
                new Dictionary<string, object?> { ["reference"] = "R2", ["currency"] = "USD" },
                new Dictionary<string, object?> { ["reference"] = null, ["currency"] = "EUR" },
                new Dictionary<string, object?> { ["reference"] = "R1", ["currency"] = "USD" }
            ]);
        Assert.Equal(
            "R1|R2",
            ReflectionTestSupport.InvokeInstance(
                Engine,
                "GetStringAggregate",
                flatTextSelection,
                "reference",
                "|",
                new List<(string FieldName, bool Descending)> { ("reference", false), ("currency", true) },
                false));
        Assert.Equal(
            "R2,R1",
            Engine.ResolveAggregateProjectionField(
                new AggregateProjection(flatTextSelection, AggregateOperator.StringAgg, null, [("reference", true)]),
                "reference"));
        Assert.Equal(
            "R2|R1",
            Engine.ResolveAggregateProjectionField(
                new AggregateProjection(flatTextSelection, AggregateOperator.StringAggDistinct, "|", [("reference", true)]),
                "reference"));
        Assert.Equal(
            "R2,R1",
            Engine.ResolveAggregateProjectionField(
                new AggregateProjection(flatTextSelection, AggregateOperator.StringAggDistinct, null, [("reference", true)]),
                "reference"));

        Assert.Equal(
            "P-ALPHA,P-BETA,P-GAMMA,P-DELTA",
            ReflectionTestSupport.InvokeInstance(
                Engine,
                "GetStringAggregate",
                new AggregateSelectionContext(peopleOrdersField, isFlat: false, ExampleData.People[0].Orders.Cast<object>().ToArray()),
                "reference",
                ",",
                new List<(string FieldName, bool Descending)>(),
                false));
        Assert.Equal(
            string.Empty,
            ReflectionTestSupport.InvokeInstance(
                Engine,
                "GetStringAggregate",
                new AggregateSelectionContext(peopleOrdersField, isFlat: false, ExampleData.People[0].Orders.Cast<object>().ToArray()),
                "missing",
                ",",
                new List<(string FieldName, bool Descending)>(),
                false));

        var simpleClauses = Assert.IsAssignableFrom<IEnumerable>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ParseSimpleOrderClauses",
            new object?[] { new Dictionary<string, object?> { ["id"] = null } }));
        Assert.Empty(simpleClauses);

        var descendingSimpleClause = Assert.IsAssignableFrom<IEnumerable>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ParseSimpleOrderClauses",
            new object?[] { new object?[] { new Dictionary<string, object?> { ["id"] = "DESC" } } }));
        Assert.Single(descendingSimpleClause);

        var groupClauses = Assert.IsAssignableFrom<IEnumerable>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ParseGroupOrderClauses",
            new object?[]
            {
                new object?[]
                {
                    new Dictionary<string, object?> { ["key"] = new Dictionary<string, object?> { ["status"] = null } },
                    new Dictionary<string, object?> { ["sum"] = new Dictionary<string, object?> { ["total"] = null } }
                }
            }));
        Assert.Empty(groupClauses);

        foreach (var name in new[] { "countDistinct", "sum", "avg", "var", "varp", "min", "max", "stdev", "stdevp", "skew", "kurtosis", "stringAgg" })
        {
            Assert.NotNull(ReflectionTestSupport.InvokeStatic(typeof(CollectionExecutionEngine), "ParseOperator", name));
        }

        Assert.NotNull(Engine.CreateAggregateSelection(
            peopleOrdersField,
            ExampleData.People[0].Orders,
            where: null,
            having: new Dictionary<string, object?> { ["not"] = new Dictionary<string, object?> { ["count"] = new Dictionary<string, object?> { ["gt"] = 100 } } },
            isFlat: false,
            expand: null));

        Assert.NotNull(Engine.CreateAggregateSelection(
            peopleOrdersField,
            ExampleData.People[0].Orders,
            where: null,
            having: new Dictionary<string, object?>
            {
                ["min"] = new Dictionary<string, object?>
                {
                    ["total"] = new Dictionary<string, object?>(),
                    ["reference"] = new Dictionary<string, object?> { ["eq"] = "P-ALPHA" }
                }
            },
            isFlat: false,
            expand: null));

        var simpleClausesWithMixedValues = Assert.IsAssignableFrom<IEnumerable>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ParseSimpleOrderClauses",
            new object?[] { new object?[] { new Dictionary<string, object?> { ["id"] = null, ["currency"] = "ASC" } } }));
        Assert.Single(simpleClausesWithMixedValues.Cast<object>());

        var groupClausesWithMixedValues = Assert.IsAssignableFrom<IEnumerable>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ParseGroupOrderClauses",
            new object?[]
            {
                new object?[]
                {
                    new Dictionary<string, object?> { ["count"] = "DESC", ["key"] = null },
                    new Dictionary<string, object?> { ["key"] = new Dictionary<string, object?> { ["status"] = null, ["currency"] = "ASC" } },
                    new Dictionary<string, object?> { ["sum"] = new Dictionary<string, object?> { ["total"] = null, ["price"] = "ASC" } }
                }
            }));
        Assert.Equal(3, groupClausesWithMixedValues.Cast<object>().Count());

        var descendingGroupClauses = Assert.IsAssignableFrom<IEnumerable>(ReflectionTestSupport.InvokeStatic(
            typeof(CollectionExecutionEngine),
            "ParseGroupOrderClauses",
            new object?[]
            {
                new object?[]
                {
                    new Dictionary<string, object?> { ["count"] = "DESC" },
                    new Dictionary<string, object?> { ["key"] = new Dictionary<string, object?> { ["currency"] = "DESC" } },
                    new Dictionary<string, object?> { ["sum"] = new Dictionary<string, object?> { ["price"] = "DESC" } }
                }
            }));
        Assert.Equal(3, descendingGroupClauses.Cast<object>().Count());

        var projectionEqualityComparer = ReflectionTestSupport.GetNestedPrivateType<IEqualityComparer<object?>>(
            typeof(CollectionExecutionEngine),
            "ProjectionEqualityComparer",
            "Instance");
        Assert.Equal(0, projectionEqualityComparer.GetHashCode(default!));

        var groupKeyComparer = ReflectionTestSupport.GetNestedPrivateType<IEqualityComparer<IReadOnlyDictionary<string, object?>>>(
            typeof(CollectionExecutionEngine),
            "GroupKeyComparer",
            "Instance");
        Assert.False(groupKeyComparer.Equals(new Dictionary<string, object?>(), null));
        Assert.False(groupKeyComparer.Equals(
            new Dictionary<string, object?> { ["id"] = 1 },
            new Dictionary<string, object?> { ["name"] = 1 }));
        Assert.False(groupKeyComparer.Equals(
            new Dictionary<string, object?> { ["id"] = 1 },
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "extra" }));

        var nullableObjectField = GetCollectionField<NullableObjectQuery>("rows");
        var filteredNullableRows = Engine.ApplyCollectionArguments(
            nullableObjectField,
            new NullableObjectQuery().Rows,
            new Dictionary<string, object?>
            {
                ["child"] = new Dictionary<string, object?> { ["name"] = new Dictionary<string, object?> { ["eq"] = "present" } }
            },
            order: null,
            offset: null,
            limit: null);
        Assert.Single(filteredNullableRows);

        var flatCoverageField = GetCollectionField<FlatCoverageQuery>("rows");
        var flatCoverageRows = Engine.ApplyFlatArguments(
            flatCoverageField,
            new FlatCoverageQuery().Rows,
            expand: ["children.leaves"],
            where: null,
            order: null,
            offset: null,
            limit: null);
        var flatCoverageRow = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(flatCoverageRows.Single());
        Assert.Contains("id", flatCoverageRow.Keys);
        Assert.Contains(flatCoverageRow.Keys, key => key.EndsWith("Value", StringComparison.Ordinal));
    }

    [Fact]
    public void Registrar_ShouldEnableProjection_ForProjectionFriendlyCollectionTypes()
    {
        var registrarType = typeof(CollectionEnhancementTypeRegistrar);
        var catalog = CollectionSchemaCatalog.CreateDefault();
        var registrar = new CollectionEnhancementTypeRegistrar(catalog);
        var ownerModel = catalog.TryGetObjectType(typeof(ProjectionFriendlyOwner));
        Assert.NotNull(ownerModel);
        var rowsField = ownerModel.FindCollection("rows");
        Assert.NotNull(rowsField);

        var configured = false;
        SchemaBuilder.New()
            .AddProjections()
            .AddType<HotChocolate.CostAnalysis.Types.CostDirectiveType>()
            .AddType<HotChocolate.CostAnalysis.Types.ListSizeDirectiveType>()
            .AddType<ProjectionFriendlyRow>()
            .AddType(new InputObjectType(descriptor =>
            {
                descriptor.Name(rowsField.FilterInputName);
                descriptor.Field("id").Type<IntType>();
            }))
            .AddType(new InputObjectType(descriptor =>
            {
                descriptor.Name(rowsField.SortInputName);
                descriptor.Field("id").Type<IntType>();
            }))
            .AddQueryType(descriptor =>
            {
                descriptor.Name("Query");
                var field = descriptor.Field("rows")
                    .Type("[ProjectionFriendlyRow!]!")
                    .Resolve(_ => Array.Empty<ProjectionFriendlyRow>());

                ReflectionTestSupport.InvokeInstance(
                    registrar,
                    "ConfigureCollectionField",
                    field,
                    rowsField);

                configured = true;
            })
            .Create();

        Assert.True(configured);

        Assert.True((bool)ReflectionTestSupport.InvokeStatic(
            registrarType,
            "CanUseProjection",
            typeof(ProjectionFriendlyRow))!);
        Assert.False((bool)ReflectionTestSupport.InvokeStatic(
            registrarType,
            "CanUseProjection",
            typeof(Security))!);
    }

    [Fact]
    public void Registrar_ShouldSuppressUnsupportedBaseOrderAndGrouping_ForScalarlessCollectionElements()
    {
        var catalog = new CollectionSchemaCatalog();
        var model = Assert.IsType<ObjectTypeModel>(
            ReflectionTestSupport.InvokeInstance(catalog, "GetOrCreateModel", typeof(SchemaEdgeQuery), true)!);
        ReflectionTestSupport.InvokeInstance(catalog, "PopulateModel", model);
        var registrar = new CollectionEnhancementTypeRegistrar(catalog);
        Assert.NotNull(model);
        var rowsField = model.FindCollection("scalarlessRows");
        Assert.NotNull(rowsField);

        Assert.False((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsOrdering", rowsField, false)!);
        Assert.False((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsGrouping", rowsField, false)!);
        Assert.True((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsOrdering", rowsField, true)!);
        Assert.True((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsGrouping", rowsField, true)!);
        Assert.True((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsFlatRows", rowsField)!);

        var queryType = SchemaBuilder.New()
            .AddProjections()
            .AddType<HotChocolate.CostAnalysis.Types.CostDirectiveType>()
            .AddType<HotChocolate.CostAnalysis.Types.ListSizeDirectiveType>()
            .AddType<ScalarlessSchemaRow>()
            .AddType(new InputObjectType(descriptor =>
            {
                descriptor.Name(rowsField.FilterInputName);
                descriptor.Field("childRows").Type<StringType>();
            }))
            .AddQueryType(descriptor =>
            {
                descriptor.Name("Query");
                var field = descriptor.Field("rows")
                    .Type("[ScalarlessSchemaRow!]!")
                    .Resolve(_ => Array.Empty<ScalarlessSchemaRow>());

                ReflectionTestSupport.InvokeInstance(registrar, "ConfigureCollectionField", field, rowsField);
            })
            .Create()
            .Print();

        Assert.Contains("rows(where:", queryType, StringComparison.Ordinal);
        Assert.DoesNotContain("order: [ScalarlessSchemaRowSortInput!]", queryType, StringComparison.Ordinal);
        Assert.DoesNotContain("ScalarlessSchemaRowGroupByInput", queryType, StringComparison.Ordinal);
    }

    [Fact]
    public void Registrar_ShouldSuppressFlatSurfaces_WhenNoExpandPathsExist()
    {
        var catalog = new CollectionSchemaCatalog();
        var model = Assert.IsType<ObjectTypeModel>(
            ReflectionTestSupport.InvokeInstance(catalog, "GetOrCreateModel", typeof(SchemaEdgeQuery), true)!);
        ReflectionTestSupport.InvokeInstance(catalog, "PopulateModel", model);
        var registrar = new CollectionEnhancementTypeRegistrar(catalog);
        Assert.NotNull(model);
        var rowsField = model.FindCollection("noExpandRows");
        Assert.NotNull(rowsField);

        Assert.False((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsFlatExpansion", rowsField)!);
        Assert.False((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsFlatRows", rowsField)!);
        Assert.False((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsOrdering", rowsField, true)!);
        Assert.False((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsGrouping", rowsField, true)!);

        var builder = new ServiceCollection()
            .AddGraphQLServer()
            .AddQueryType(descriptor => descriptor.Name("Query").Field("ping").Type<StringType>().Resolve("pong"));
        ReflectionTestSupport.InvokeInstance(registrar, "RegisterFlatTypes", builder, rowsField);

        var registrarType = typeof(CollectionEnhancementTypeRegistrar);
        var flatScalarFieldType = registrarType.GetNestedType("FlatScalarFieldDefinition", BindingFlags.NonPublic)!;
        var emptyFields = Array.CreateInstance(flatScalarFieldType, 0);
        ReflectionTestSupport.InvokeStatic(registrarType, "RegisterGroupByEnum", builder, "UnusedEmptyGroupBy", emptyFields);
    }

    [Fact]
    public void Registrar_ShouldDisableFlatOrdering_WhenExpandPathsExistWithoutScalarFields()
    {
        var catalog = new CollectionSchemaCatalog();
        var model = Assert.IsType<ObjectTypeModel>(
            ReflectionTestSupport.InvokeInstance(catalog, "GetOrCreateModel", typeof(ExpandOnlyQuery), true)!);
        ReflectionTestSupport.InvokeInstance(catalog, "PopulateModel", model);
        var registrar = new CollectionEnhancementTypeRegistrar(catalog);
        Assert.NotNull(model);
        var rowsField = model.FindCollection("rows");
        Assert.NotNull(rowsField);

        Assert.True((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsFlatExpansion", rowsField)!);
        Assert.False((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsOrdering", rowsField, true)!);
        Assert.False((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsGrouping", rowsField, true)!);
        Assert.False((bool)ReflectionTestSupport.InvokeInstance(registrar, "SupportsFlatRows", rowsField)!);
    }

    [Fact]
    public void ResolverArgumentReader_ShouldCoverLiteralVariableAndFallbackParsing()
    {
        var field = GetFieldNode("""
            query Demo(
              $intValue: Int
              $directInt: Int
              $listValue: [String!]
            ) {
              demo(
                directInt: $directInt
                longInt: 2
                floatInt: 3.5
                stringInt: "4"
                variableList: $listValue
              )
            }
            """);
        var context = ReflectionTestSupport.CreateResolverContext(
            field,
            arguments: new Dictionary<string, object?>
            {
                ["directInt"] = 1,
                ["variableList"] = new object?[] { "A", null, "B" }
            });

        Assert.Equal(1, ResolverArgumentReader.GetNullableInt(context, "directInt"));
        Assert.Equal(2, ResolverArgumentReader.GetNullableInt(context, "longInt"));
        Assert.Equal(3, ResolverArgumentReader.GetNullableInt(context, "floatInt"));
        Assert.Equal(4, ResolverArgumentReader.GetNullableInt(context, "stringInt"));
        Assert.Equal(["A", "B"], ResolverArgumentReader.GetStringList(context, "variableList"));
        Assert.Null(ResolverArgumentReader.GetRequiredString(context, "missing"));

        var directive = new DirectiveNode(
            null,
            new NameNode("export"),
            [
                new ArgumentNode(new NameNode("none"), new NullValueNode(null)),
                new ArgumentNode(new NameNode("variable"), new VariableNode(null, new NameNode("v"))),
                new ArgumentNode(new NameNode("enumValue"), new EnumValueNode("CSV"))
            ]);
        Assert.Null(ResolverArgumentReader.GetDirectiveArgument(directive, "none"));
        Assert.Null(ResolverArgumentReader.GetDirectiveArgument(directive, "variable"));
        Assert.Equal("CSV", ResolverArgumentReader.GetDirectiveArgument(directive, "enumValue"));

        Assert.Null(ReflectionTestSupport.InvokeStatic(typeof(ResolverArgumentReader), "ParseValue", new NullValueNode(null)));
        Assert.Null(ReflectionTestSupport.InvokeStatic(typeof(ResolverArgumentReader), "ParseValue", new VariableNode(null, new NameNode("v"))));
        var customValueNode = ReflectionTestSupport.Create<IValueNode>((method, _) => method.Name switch
        {
            "get_Value" => "custom",
            "get_Kind" => SyntaxKind.StringValue,
            "get_Location" => null!,
            "GetNodes" => Array.Empty<ISyntaxNode>(),
            "ToString" => "custom",
            _ => null
        });
        Assert.Equal("custom", ReflectionTestSupport.InvokeStatic(typeof(ResolverArgumentReader), "ParseValue", customValueNode));
    }

    private static IOperationResult CreateOperationResult(string json, IReadOnlyList<IError>? errors = null) =>
        ReflectionTestSupport.Create<IOperationResult>((method, _) => method.Name switch
        {
            "ToJson" => json,
            "get_Errors" => errors,
            _ => method.ReturnType.IsValueType ? RuntimeHelpers.GetUninitializedObject(method.ReturnType) : null
        });

    private static FieldNode GetFieldNode(string documentText) =>
        Utf8GraphQLParser.Parse(documentText)
            .Definitions
            .OfType<OperationDefinitionNode>()
            .Single()
            .SelectionSet.Selections
            .OfType<FieldNode>()
            .Single();

    private static CollectionFieldModel GetCollectionField<THost>(string fieldName)
    {
        var model = Catalog.TryGetObjectType(typeof(THost));
        Assert.NotNull(model);
        var field = model.FindCollection(fieldName);
        Assert.NotNull(field);
        return field;
    }

    public sealed class UnknownCollectionHost
    {
        public required Guid[] Values { get; init; }
    }

    public sealed record NullableChild(string Name);

    public sealed record NullableObjectRow(int Id, NullableChild? Child);

    public sealed class NullableObjectQuery
    {
        public NullableObjectRow[] Rows { get; } =
        [
            new(1, null),
            new(2, new NullableChild("present"))
        ];
    }

    public sealed record FlatCoverageLeaf(int Value)
    {
        public int WriteOnly
        {
            set { }
        }
    }

    public sealed record FlatCoverageChild(FlatCoverageLeaf[] Leaves)
    {
        public string HiddenObject => "ignored";
    }

    public sealed class FlatCoverageBaseRow
    {
        public int Id { get; init; }

        public FlatCoverageChild[] Children { get; init; } = [];

        public FlatCoverageChild ObjectChild { get; init; } = new([]);

        public int WriteOnly
        {
            set { }
        }
    }

    public sealed class FlatCoverageQuery
    {
        public FlatCoverageBaseRow[] Rows { get; } =
        [
            new()
            {
                Id = 1,
                Children =
                [
                    new FlatCoverageChild([new FlatCoverageLeaf(7)])
                ],
                ObjectChild = new FlatCoverageChild([])
            }
        ];
    }

    public sealed class ProjectionFriendlyOwner
    {
        public ProjectionFriendlyRow[] Rows { get; } =
        [
            new() { Id = 1, Name = "Row" }
        ];
    }

    public sealed class SchemaEdgeQuery
    {
        public ScalarlessSchemaRow[] GetScalarlessRows() =>
        [
            new() { ChildRows = [new ScalarlessSchemaChild(1)] }
        ];

        public NoExpandSchemaRow[] GetNoExpandRows() =>
        [
            new() { Id = 1, Name = "row" }
        ];
    }

    public sealed class ExpandOnlyQuery
    {
        public ExpandOnlyRow[] GetRows() => [new() { Children = [new ExpandOnlyLeaf()] }];
    }

    public sealed class ScalarlessSchemaRow
    {
        public ScalarlessSchemaChild[] ChildRows { get; init; } = [];
    }

    public sealed record ScalarlessSchemaChild(int Value);

    public sealed class NoExpandSchemaRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    public sealed class ExpandOnlyRow
    {
        public ExpandOnlyLeaf[] Children { get; init; } = [];
    }

    public sealed class ExpandOnlyLeaf;

    public sealed class ProjectionFriendlyRow
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }

    private sealed class UnreadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }

    public sealed record NullExpandLeaf(int Id);

    public sealed record NullExpandBranch(NullExpandLeaf[] Rows);

    public sealed record NullExpandOuter(NullExpandBranch? Maybe);

    public sealed class NullExpandQuery
    {
        public NullExpandOuter[] NullableRows { get; } =
        [
            new(new NullExpandBranch([new NullExpandLeaf(1)])),
            new(null)
        ];
    }

}
