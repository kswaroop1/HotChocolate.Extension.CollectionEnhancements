using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Serialization;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;

namespace HotChocolate.Extension.CollectionEnhancements.Schema;

internal sealed class CollectionEnhancementHttpResponseFormatter(ITimeProvider timeProvider) : IHttpResponseFormatter
{
    private readonly DefaultHttpResponseFormatter _fallback = new(indented: false, encoder: JavaScriptEncoder.Default, timeProvider);

    public GraphQLRequestFlags CreateRequestFlags(AcceptMediaType[] acceptMediaTypes) =>
        _fallback.CreateRequestFlags(acceptMediaTypes);

    public ValueTask FormatAsync(
        HttpResponse response,
        ISchema schema,
        ulong version,
        CancellationToken cancellationToken) =>
        _fallback.FormatAsync(response, schema, version, cancellationToken);

    public ValueTask FormatAsync(
        HttpResponse response,
        IExecutionResult result,
        AcceptMediaType[] acceptMediaTypes,
        HttpStatusCode? proposedStatusCode,
        CancellationToken cancellationToken)
    {
        if (result is IOperationResult operationResult &&
            operationResult.Errors is null or { Count: 0 } &&
            response.HttpContext.Items.TryGetValue(ExportContextDataKeys.CsvExportPlan, out var planValue) &&
            planValue is CsvExportPlan plan)
        {
            return WriteCsvAsync(response, operationResult, plan, proposedStatusCode, cancellationToken);
        }

        return _fallback.FormatAsync(response, result, acceptMediaTypes, proposedStatusCode, cancellationToken);
    }

    private static async ValueTask WriteCsvAsync(
        HttpResponse response,
        IOperationResult result,
        CsvExportPlan plan,
        HttpStatusCode? proposedStatusCode,
        CancellationToken cancellationToken)
    {
        response.StatusCode = (int)(proposedStatusCode ?? HttpStatusCode.OK);
        response.ContentType = "text/csv; charset=utf-8";

        if (!string.IsNullOrWhiteSpace(plan.FileName))
        {
            response.Headers.ContentDisposition = $"attachment; filename=\"{EscapeFileName(plan.FileName)}\"";
        }

        var payload = BuildCsvPayload(result, plan);
        await response.WriteAsync(payload, Encoding.UTF8, cancellationToken);
    }

    private static string BuildCsvPayload(IOperationResult result, CsvExportPlan plan)
    {
        using var json = JsonDocument.Parse(result.ToJson());
        if (!json.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty(plan.RootResponseName, out var root))
        {
            return plan.IncludeHeader
                ? string.Join(plan.Separator, plan.Columns.Select(column => EscapeCell(column.Header, plan.Separator[0])))
                : string.Empty;
        }

        var lines = new List<string>();

        if (plan.IncludeHeader)
        {
            lines.Add(string.Join(plan.Separator, plan.Columns.Select(column => EscapeCell(column.Header, plan.Separator[0]))));
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in root.EnumerateArray())
            {
                lines.Add(BuildRow(row, plan));
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            lines.Add(BuildRow(root, plan));
        }

        return string.Join('\n', lines);
    }

    private static string BuildRow(JsonElement row, CsvExportPlan plan) =>
        string.Join(
            plan.Separator,
            plan.Columns.Select(column =>
                EscapeCell(ReadScalarValue(row, column.ResponsePath), plan.Separator[0])));

    private static string ReadScalarValue(JsonElement row, IReadOnlyList<string> path)
    {
        var current = row;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return string.Empty;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => current.GetString()!,
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            _ => current.ToString()
        };
    }

    private static string EscapeCell(string value, char separator)
    {
        if (!value.Contains(separator) &&
            !value.Contains('"') &&
            !value.Contains('\n') &&
            !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string EscapeFileName(string fileName) =>
        fileName.Replace("\"", string.Empty, StringComparison.Ordinal);
}
