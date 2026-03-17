using System.Text.Json;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

public sealed class AggregateContractTests
{
    [Fact]
    public async Task Aggregate_EmptySelection_ShouldHonorDocumentedNullAndZeroSemantics()
    {
        var executor = await TestServerFactory.CreateTestExecutorAsync();

        var result = await executor.ExecuteQueryResultAsync("""
            query {
              customers(where: { id: { eq: 1 } }) {
                id
                ordersAggregate(where: { reference: { eq: "MISSING" } }) {
                  count
                  countDistinct {
                    reference
                  }
                  sum {
                    total
                  }
                  avg {
                    total
                  }
                  var {
                    total
                  }
                  varp {
                    total
                  }
                  min {
                    total
                    createdAt
                  }
                  max {
                    total
                    createdAt
                  }
                  stdev {
                    total
                  }
                  stdevp {
                    total
                  }
                  skew {
                    total
                  }
                  kurtosis {
                    total
                  }
                  stringAgg(separator: ",") {
                    reference
                  }
                  stringAggDistinct(separator: ",") {
                    reference
                  }
                }
              }
            }
            """);

        using var json = result.AssertSuccessfulJson();
        var aggregate = json.RootElement
            .GetProperty("data")
            .GetProperty("customers")
            .EnumerateArray()
            .Single()
            .GetProperty("ordersAggregate");

        Assert.Equal(0, aggregate.GetProperty("count").GetInt32());
        Assert.Equal(0, aggregate.GetProperty("countDistinct").GetProperty("reference").GetInt32());
        AssertPropertyIsNull(aggregate.GetProperty("sum"), "total");
        AssertPropertyIsNull(aggregate.GetProperty("avg"), "total");
        AssertPropertyIsNull(aggregate.GetProperty("var"), "total");
        AssertPropertyIsNull(aggregate.GetProperty("varp"), "total");
        AssertPropertyIsNull(aggregate.GetProperty("min"), "total");
        AssertPropertyIsNull(aggregate.GetProperty("min"), "createdAt");
        AssertPropertyIsNull(aggregate.GetProperty("max"), "total");
        AssertPropertyIsNull(aggregate.GetProperty("max"), "createdAt");
        AssertPropertyIsNull(aggregate.GetProperty("stdev"), "total");
        AssertPropertyIsNull(aggregate.GetProperty("stdevp"), "total");
        AssertPropertyIsNull(aggregate.GetProperty("skew"), "total");
        AssertPropertyIsNull(aggregate.GetProperty("kurtosis"), "total");
        Assert.Equal(string.Empty, aggregate.GetProperty("stringAgg").GetProperty("reference").GetString());
        Assert.Equal(string.Empty, aggregate.GetProperty("stringAggDistinct").GetProperty("reference").GetString());
    }

    [Fact]
    public async Task Aggregate_And_Group_Having_ShouldHonorNullAndEmptyArraySemantics()
    {
        var executor = await TestServerFactory.CreateTestExecutorAsync();

        var result = await executor.ExecuteQueryResultAsync("""
            query {
              customers(where: { id: { eq: 1 } }) {
                id
                ordersAggregate(having: { count: { gt: 10 } }) {
                  count
                }
                ordersGroup(
                  by: [status]
                  having: { count: { gt: 10 } }
                ) {
                  key {
                    status
                  }
                  count
                }
              }
            }
            """);

        using var json = result.AssertSuccessfulJson();
        var customer = json.RootElement
            .GetProperty("data")
            .GetProperty("customers")
            .EnumerateArray()
            .Single();

        Assert.Equal(JsonValueKind.Null, customer.GetProperty("ordersAggregate").ValueKind);
        Assert.Equal(0, customer.GetProperty("ordersGroup").GetArrayLength());
    }

    private static void AssertPropertyIsNull(JsonElement element, string propertyName) =>
        Assert.Equal(JsonValueKind.Null, element.GetProperty(propertyName).ValueKind);
}
