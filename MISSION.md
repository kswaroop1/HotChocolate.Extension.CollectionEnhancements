# MISSION: Extend C# HotChocolate GraphQL Server To Add Functionality For All Collections (at root or nested inner collections)

The goal is to enhance the existing HotChocolate GraphQL server capabilities for collection type members, particularly for developers using Implementation-First methodology:
- add optional filtering, sorting, and slicing (the ability to skip or take a certain number of elements (after applying filtering, sorting, etc.) from the beginning or end of filtered/sorted collection types).
  . these operations are inspired from LINQ and should be intuitive for developers familiar with C#.
- add grouping and aggregation capabilities for collection types, allowing developers to perform operations like counting, summing, or averaging values within collections.
  . these operations are inspired from SQL aggregation capabilities, but adapt them to the GraphQL context.
- at root level, allow filter conditions based on the inner collection filtered size, enabling queries like "find all users with more than 5 posts".
- provide @flatten directive for flattening nested collections, where elements from parent level are repeated in result set (like cross join)

Note: Pagination is already supported by HotChocolate, so it will not be included in this extension

The implementation must be:
- flexible enough to handle both top-level collections and nested collections within complex objects.
- the extended schema must be backwards compatible, meaning existing queries should not break.
- the extended schema must provide for natural GraphQL query syntax, allowing developers to use the new features intuitively. 

# PERSONA

You are a senior software engineer with extensive experience in C# and GraphQL, particularly with the HotChocolate library. You have a deep understanding of GraphQL concepts such as queries, mutations, subscriptions, and schema design. Your expertise includes implementing complex filtering, sorting, and pagination mechanisms for various data structures.  You use modern C# features and design patterns to create maintainable and scalable code.

You break down complex problems into manageable tasks, ensuring that each component is well-designed and adheres to best practices. You are familiar with both Implementation-First and Schema-First methodologies in GraphQL, but you prefer Implementation-First for its flexibility and ease of use in dynamic schema generation.

# OVERALL ARCHITECTURE

- **Language**: C#
- **Framework**: .Net 9, HotChocolate GraphQL
- **Implementation**: 
  - Strategy:
    - The implementation should heavily favor extending IQueryable expression trees to push down filtering, sorting, and aggregation logic
      - This will naturally translate to the underlying database if the provider supports it, avoiding in-memory processing wherever possible for performance.
    - Utilize existing HotChocolate features and middleware to minimize custom code, esp. for dynamically generating the schema (as we prefer Implementation-First methodology).
  - Tactics:
    - Leverage .NET 9 features for improved performance and maintainability.
    - Review TASKS.md, after completion of each sub-task, create a git commit.
  - Error Handling:
    - Implement robust error handling to manage invalid queries or unsupported operations gracefully, such as applying aggregation functions to incompatible data types
- **Tests**:
  - Write unit and integration tests to ensure functionality and backward compatibility.
  - Include tests for error handling and validation scenarios.
  - For integration tests:
    - use a mock database or an in-memory database to simulate real-world scenarios without needing a full database setup.
    - ensure extensive test coverage for all new features and edge cases.
    - the tests should use a GraphQL client with actual queries to execute queries against the schema and verify the results.
    - organize test queries by feature, ensuring that each test case is clear and focused on a specific aspect of the functionality.
- **Project Structure**: Consider ONE of the following TWO, evaluate based on long term maintainability.
  - **Tech Driven**:
    - **GraphQL Schema**: Define the schema extensions for collections.
    - **Resolvers**: Implement resolvers for filtering, sorting, and aggregation.
    - **Directives**: Create custom directives for flattening collections.
    - **Middleware**: Add middleware for handling collection size filtering.
  - **Feature Driven**: kind of like vertical slice
    - **Collection Enhacements**: filter, sort, first, last, skip, take: LINQ inspired features
    - **Grouping And Aggregation
    - **Root Level Collections Filter On Nested Collection Filter Size**
    - **Flattening Of Nested Collections**
    - **_Common Utility Functions_**: code common to implement the features above

# EXAMPLE USAGE

Given a GraphQL schema generated from the following C# classes:

```csharp
public record Coupon(DateOnly ObservationDate, DateOnly PaymentDate, decimal? InterestRate);
public record Call(DateOnly ObservationDate, DateOnly CallDate, bool IsCalled);
public record Underlying(int Id, string Ric, string Currency, decimal StrikePrice);
public record SecurityDetails(Underlying[] Underlyings, Coupon[] Coupons, Call[] Calls);
public record Security(int Id, string Isin, string Currency, decimal Price, decimal Notional, DateOnly StrikeDate, DateOnly ExpirationDate, SecurityDetails Details);

// The service interface to fetch securities.
// Its implementation is not relevant to this project.
interface ISecurityService
{
    IQueryable<Security> GetSecurities();
}

public class Query // The root query type registered with HotChocolate
{
    [UseFiltering]
    [UseSorting]
    [UseProjection]
    [UsePaging]
    public IQueryable<Security> GetSecurities([Service] ISecurityService securityService)
        => securityService.GetSecurities();
}
```

The extended schema should allow for the following queries, demonstrating the new capabilities:

```graphql
# query: #01: Backward compatibility (standard filtering)
# STATUS: Must be maintained.
query {
  securities(where: { currency: { eq: "USD" } }) {
    id isin currency
  }
}

# query: #N01: Filtering, sorting, and slicing on nested collections
# STATUS: This design is sound and will be implemented.
query {
  securities(where: { id: { eq: 1 } }) {
    id
    details {
      coupons(
        where: { interestRate: { gt: 0.05 } },
        order: [{ observationDate: DESC }],
        skip: 2,
        take: 3
      ) {
        observationDate
        paymentDate
        interestRate
      }
    }
  }
}

# query: #N02: Aggregation capabilities (NEW DESIGN)
# STATUS: This is the new, approved design for aggregations.
# It uses explicit 'xxxAggregation' fields and a nested 'aggregates' object
# for clarity and type safety.
query {
  securities(where: { id: { eq: 1 } }) {
    id
    details {
      # Example 1: Group underlyings by currency
      underlyingsAggregation(
        group: [currency]
        having: { aggregates: { count: { gt: 1 } } }
        order: [{ currency: ASC }]
      ) {
        # Grouping key(s) are top-level fields
        currency
        # Aggregate results are in a nested object
        aggregates {
          count
          avg { strikePrice }
          stringAgg(separator: ", ") { ric }
        }
      }

      # Example 2: Aggregations over an entire collection (no grouping)
      couponsAggregation(where: { interestRate: { gt: 0.05 } }) {
        aggregates {
          sum { interestRate }
          max { paymentDate }
        }
      }

      # Example 3: Conditional counts within a single aggregation
      callsAggregation {
        aggregates {
          called: count(where: { isCalled: { eq: true } })
          notCalled: count(where: { isCalled: { eq: false } })
        }
      }
    }
  }
}

# query: #N03: Root level filtering based on inner collection filtered size
# STATUS: This design is sound and will be implemented.
query ($date: LocalDate!, $number: Int!) {
  securities(where: {
    details: {
      coupons: {
        where: { paymentDate: { gt: $date } },
        count: { gt: $number }
      }
    }
  }) {
    id
    details {
      # The nested collection can still be queried independently
      coupons {
        paymentDate
      }
    }
  }
}

# query: #N04: Flattening nested collections with @flatten
# STATUS: This is the new, approved design for the @flatten directive.
# It is applied to a specific field and uses a prefix.
query {
  securities(where: { id: { eq: 1 } }) {
    id
    isin
    # The @flatten directive is applied directly to the collection field
    # and transforms the result into a table-like structure.
    @flatten(prefix: "coupon")
    details {
      coupons(take: 5) {
        # You still select the fields to be flattened
        observationDate
        paymentDate
      }
    }
  }
}

"""# query: #N05: Combined features: Multi-level, cross-product flattening
# STATUS: This is the ultimate composite query, demonstrating how the @flatten
# directive can be applied to a parent field to orchestrate a cross-join
# between multiple, nested child collections (including aggregations).
query CrossProductFlatten {
  securities(where: { id: { eq: 1 } }) {
    # 1. Select the top-level fields. These will be repeated for each
    #    row in the final flattened, cross-joined result.
    id
    isin

    # 2. Apply the @flatten directive to the PARENT field (`details`) that
    #    contains the collections you want to cross-join.
    details @flatten(
      # 3. Provide a list of paths to the child collections to flatten.
      paths: [
        {
          field: "couponsAggregation"
          prefix: "cpn_agg"
        },
        {
          field: "calls"
          prefix: "call"
        }
      ]
    ) {
      # 4. Define the data for each collection as usual. The @flatten
      #    middleware will execute these resolvers and use their results
      #    to construct the final flattened output.

      # This becomes `cpn_agg_min_observationDate`
      couponsAggregation {
        aggregates {
          min { observationDate }
        }
      }

      # This becomes `call_callDate`
      calls(take: 5) {
        callDate
      }
    }
  }
}
""

```
