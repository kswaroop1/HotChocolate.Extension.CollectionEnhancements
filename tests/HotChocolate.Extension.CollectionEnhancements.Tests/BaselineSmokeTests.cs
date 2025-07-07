using HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

public sealed class BaselineSmokeTests
{
    [Fact]
    public async Task SharedExampleDomain_RootQueries_ShouldWork()
    {
        var executor = await TestServerFactory.CreateTestExecutorAsync();

        var result = await executor.ExecuteQueryResultAsync("""
            query {
              securities(order: [{ id: ASC }]) {
                id
                isin
              }
              customers(order: [{ id: ASC }]) {
                id
                name
              }
              people(order: [{ id: ASC }]) {
                id
                name
              }
            }
            """);

        using var json = result.AssertSuccessfulJson();
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(4, data.GetProperty("securities").GetArrayLength());
        Assert.Equal(3, data.GetProperty("customers").GetArrayLength());
        Assert.Equal(2, data.GetProperty("people").GetArrayLength());
    }

    [Fact]
    public async Task Securities_Filtering_And_Sorting_ShouldWork()
    {
        var executor = await TestServerFactory.CreateTestExecutorAsync();

        var result = await executor.ExecuteQueryResultAsync("""
            query {
              securities(
                where: { currency: { in: ["USD", "EUR"] } }
                order: [{ id: DESC }]
              ) {
                id
                isin
                currency
              }
            }
            """);

        using var json = result.AssertSuccessfulJson();
        var rows = json.RootElement
            .GetProperty("data")
            .GetProperty("securities")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal(2, rows[0].GetProperty("id").GetInt32());
        Assert.Equal("EUR", rows[0].GetProperty("currency").GetString());
        Assert.Equal(1, rows[1].GetProperty("id").GetInt32());
        Assert.Equal("USD", rows[1].GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Nested_Example_Domain_Data_ShouldBeQueryable()
    {
        var executor = await TestServerFactory.CreateTestExecutorAsync();

        var result = await executor.ExecuteQueryResultAsync("""
            query {
              customers(where: { id: { eq: 1 } }) {
                id
                name
                orders {
                  id
                  total
                  status
                  reference
                  items {
                    id
                    productName
                    quantity
                    tags {
                      id
                      tagName
                      category
                    }
                  }
                }
              }
              people(where: { id: { eq: 1 } }) {
                id
                name
                posts {
                  id
                  title
                  comments {
                    id
                    body
                    author
                  }
                }
                orders {
                  id
                  total
                  status
                  items {
                    id
                    tags {
                      id
                      tagName
                    }
                  }
                }
              }
            }
            """);

        using var json = result.AssertSuccessfulJson();
        var data = json.RootElement.GetProperty("data");

        var firstCustomer = data.GetProperty("customers").EnumerateArray().First();
        Assert.Equal("Alice Capital", firstCustomer.GetProperty("name").GetString());
        Assert.Equal(4, firstCustomer.GetProperty("orders").GetArrayLength());

        var firstPerson = data.GetProperty("people").EnumerateArray().First();
        Assert.Equal("Eve Trader", firstPerson.GetProperty("name").GetString());
        Assert.Equal(2, firstPerson.GetProperty("posts").GetArrayLength());
        Assert.Equal(4, firstPerson.GetProperty("orders").GetArrayLength());
    }
}
