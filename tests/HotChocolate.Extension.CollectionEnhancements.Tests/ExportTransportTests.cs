using System.Net;
using System.Text;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Serialization;
using HotChocolate.Execution;
using HotChocolate.Extension.CollectionEnhancements.Schema;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

public sealed class ExportTransportTests
{
    [Fact]
    public async Task RootFlatExport_ShouldSerializeCsv()
    {
        var query = """
            query {
              securitiesFlat(
                expand: ["details.coupons"]
                order: [{ couponPaymentDate: ASC }]
              ) @export(format: CSV, separator: ";", fileName: "coupon-rows.csv") {
                securityId: id
                isin
                paymentDate: couponPaymentDate
                rate: couponInterestRate
              }
            }
            """;

        var formatted = await FormatAsync(query);

        Assert.True(formatted.Context.Items.ContainsKey(ExportContextDataKeys.CsvExportPlan));
        Assert.Equal("text/csv; charset=utf-8", formatted.Context.Response.ContentType);
        Assert.Equal((int)HttpStatusCode.OK, formatted.Context.Response.StatusCode);
        Assert.Equal("attachment; filename=\"coupon-rows.csv\"", formatted.Context.Response.Headers.ContentDisposition.ToString());

        var lines = formatted.Body.Split('\n');
        Assert.Equal("securityId;isin;paymentDate;rate", lines[0]);
        Assert.Equal(16, lines.Length);
        Assert.Equal("1;US123456789;20/03/2023;0.05", lines[1]);
        Assert.Equal("1;US123456789;20/06/2024;0.035", lines[^1]);
        Assert.DoesNotContain("\"data\"", formatted.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootFlatGroupExport_ShouldSerializeCsv()
    {
        var query = """
            query {
              securitiesFlatGroup(
                expand: ["details.coupons"]
                by: [couponPaymentDate]
                order: [{ key: { couponPaymentDate: ASC } }]
              ) @export(format: CSV, separator: ",", fileName: "coupon-groups.csv") {
                key {
                  paymentDate: couponPaymentDate
                }
                groupCount: count
                avg {
                  avgRate: couponInterestRate
                }
              }
            }
            """;

        var formatted = await FormatAsync(query);

        Assert.True(formatted.Context.Items.ContainsKey(ExportContextDataKeys.CsvExportPlan));
        Assert.Equal("text/csv; charset=utf-8", formatted.Context.Response.ContentType);
        Assert.Equal("paymentDate,groupCount,avgRate", formatted.Body.Split('\n')[0]);
        Assert.Contains("20/03/2023,1,0.05", formatted.Body, StringComparison.Ordinal);
        Assert.Contains("05/04/2024,1,", formatted.Body, StringComparison.Ordinal);
        Assert.Contains("20/06/2024,1,0.035", formatted.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""
        query {
          securitiesFlatAggregate(
            expand: ["details.coupons"]
          ) @export(format: CSV, separator: ",") {
            count
          }
        }
        """)]
    [InlineData("""
        query {
          securitiesFlat(
            expand: ["details.coupons"]
          ) @export(format: CSV, separator: ",") {
            id
          }
          customers {
            id
          }
        }
        """)]
    [InlineData("""
        query {
          securitiesFlat(
            expand: ["details.coupons"]
          ) @export(format: CSV, separator: "||") {
            id
            duplicate: isin
            duplicate: couponPaymentDate
          }
        }
        """)]
    public async Task InvalidExportQueries_ShouldRemainJsonErrors_AndEmitNoCsv(string query)
    {
        var formatted = await FormatAsync(query);

        Assert.True(formatted.Result.Errors is { Count: > 0 }, formatted.Result.ToJson());
        Assert.Contains("json", formatted.Context.Response.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("{", formatted.Body, StringComparison.Ordinal);
        Assert.Contains("\"errors\"", formatted.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("text/csv", formatted.Context.Response.ContentType, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<FormattedResponse> FormatAsync(string query)
    {
        var services = TestServerFactory.CreateTestServices();
        var executor = await services.GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();

        var formatter = services.GetRequiredService<IHttpResponseFormatter>();
        var result = await executor.ExecuteQueryResultAsync(query);

        var context = new DefaultHttpContext
        {
            RequestServices = services
        };

        var exportPlan = CsvExportPlanBuilder.TryCreate(Utf8GraphQLParser.Parse(query), operationName: null);
        if (exportPlan is not null)
        {
            context.Items[ExportContextDataKeys.CsvExportPlan] = exportPlan;
        }

        context.Response.Body = new MemoryStream();

        await formatter.FormatAsync(
            context.Response,
            result,
            [],
            proposedStatusCode: null,
            cancellationToken: CancellationToken.None);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        return new FormattedResponse(context, result, body);
    }

    private sealed record FormattedResponse(
        DefaultHttpContext Context,
        IOperationResult Result,
        string Body);
}
