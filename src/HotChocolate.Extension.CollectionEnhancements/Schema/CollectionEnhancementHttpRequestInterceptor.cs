using System.Text.Json;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;

namespace HotChocolate.Extension.CollectionEnhancements.Schema;

internal sealed class CollectionEnhancementHttpRequestInterceptor : DefaultHttpRequestInterceptor
{
    public override async ValueTask OnCreateAsync(
        HttpContext context,
        IRequestExecutor requestExecutor,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken)
    {
        var exportPlan = await TryCreateExportPlanAsync(context.Request, cancellationToken);
        if (exportPlan is not null)
        {
            context.Items[ExportContextDataKeys.CsvExportPlan] = exportPlan;
        }

        await base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
    }

    private static async ValueTask<CsvExportPlan?> TryCreateExportPlanAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var payload = await ReadRequestPayloadAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(payload.Query))
        {
            return null;
        }

        var document = Utf8GraphQLParser.Parse(payload.Query);
        return CsvExportPlanBuilder.TryCreate(document, payload.OperationName);
    }

    private static async ValueTask<GraphQlRequestPayload> ReadRequestPayloadAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Query.TryGetValue("query", out var query))
        {
            return new GraphQlRequestPayload(
                query.ToString(),
                request.Query.TryGetValue("operationName", out var operationName)
                    ? operationName.ToString()
                    : null);
        }

        if (request.Body is null || !request.Body.CanRead)
        {
            return default;
        }

        request.EnableBuffering();
        request.Body.Position = 0;

        try
        {
            using var json = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            var queryText = json.RootElement.TryGetProperty("query", out var queryElement)
                ? queryElement.GetString()
                : null;
            var operationName = json.RootElement.TryGetProperty("operationName", out var operationNameElement)
                ? operationNameElement.GetString()
                : null;

            return new GraphQlRequestPayload(queryText, operationName);
        }
        catch (JsonException)
        {
            return default;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private readonly record struct GraphQlRequestPayload(
        string? Query,
        string? OperationName);
}
