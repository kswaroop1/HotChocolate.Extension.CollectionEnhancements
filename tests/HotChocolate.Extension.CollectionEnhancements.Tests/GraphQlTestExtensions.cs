using System.Text.Json;
using HotChocolate.Execution;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

internal static class GraphQlTestExtensions
{
    public static async Task<IOperationResult> ExecuteQueryResultAsync(
        this IRequestExecutor executor,
        string query)
    {
        var result = await executor.ExecuteAsync(query);
        return Assert.IsAssignableFrom<IOperationResult>(result);
    }

    public static JsonDocument AssertSuccessfulJson(this IOperationResult result)
    {
        Assert.True(result.Errors is null or { Count: 0 }, result.ToJson());
        return JsonDocument.Parse(result.ToJson());
    }

    public static void AssertHasErrors(this IOperationResult result)
    {
        Assert.True(result.Errors is { Count: > 0 }, result.ToJson());
    }
}
