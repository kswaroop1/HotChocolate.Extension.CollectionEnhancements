# Aggregation - Usage Examples

These examples are intended to be executable against the shared example domain in
`MISSION.md`.

They are normative acceptance-test queries. Unless a feature explicitly
documents a different query shape, collection fields are selected directly and
do not use an `items` wrapper.

## Basic Setup

```csharp
services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddFiltering()
    .AddSorting()
    .AddProjections()
    .AddCollectionEnhancements();
```

## Raw Collection Querying

```graphql
query {
  people {
    orders(
      where: { status: { eq: ACTIVE } }
      order: [{ createdAt: DESC }]
      offset: 10
      limit: 5
    ) {
      id
      total
      createdAt
    }
  }
}
```

## Single Aggregate Row

```graphql
query {
  people {
    ordersAggregate(where: { status: { eq: ACTIVE } }) {
      orderCount: count
      totalSales: sum {
        total
      }
      averageOrderValue: avg {
        total
      }
      totalSalesStdev: stdev {
        total
      }
      totalSalesStdevp: stdevp {
        total
      }
    }
  }
}
```

## Higher Moments on Numeric Fields

```graphql
query {
  people {
    ordersAggregate {
      totalSalesSkew: skew {
        total
      }
      totalSalesKurtosis: kurtosis {
        total
      }
    }
  }
}
```

## Distinct Count and String Aggregation

```graphql
query {
  people {
    ordersAggregate {
      uniqueReferences: countDistinct {
        reference
      }
      orderRefsCsv: stringAgg(separator: ", ", order: [{ reference: ASC }]) {
        reference
      }
      distinctOrderRefsCsv: stringAggDistinct(separator: ";") {
        reference
      }
    }
  }
}
```

## Multi-Member Operator Projection

```graphql
query {
  securitiesAggregate {
    countDistinct {
      currency
      isin
    }
    min {
      price
      expirationDate
    }
    max {
      price
      expirationDate
    }
  }
}
```

## Grouped Aggregation

```graphql
query {
  customers {
    ordersGroup(
      by: [status]
      order: [
        { key: { status: ASC } }
        { sum: { total: DESC } }
      ]
    ) {
      key {
        status
      }
      count
      sum {
        total
      }
      avg {
        total
      }
      stdev {
        total
      }
    }
  }
}
```

## Grouped Aggregation with `having`

```graphql
query {
  customers {
    ordersGroup(
      by: [status]
      having: {
        and: [
          { count: { gte: 2 } }
          { stdev: { total: { lt: 50 } } }
        ]
      }
      order: [
        { key: { status: ASC } }
        { count: DESC }
      ]
      limit: 10
    ) {
      key {
        status
      }
      count
      sum {
        total
      }
    }
  }
}
```

## Conditional Count

```graphql
query {
  customers {
    ordersAggregate {
      cancelledOrders: count(where: { status: { eq: CANCELLED } })
      activeOrders: count(where: { status: { eq: ACTIVE } })
    }
  }
}
```

## Aggregation over the Security Domain

```graphql
query {
  securities {
    id
    details {
      couponsAggregate {
        couponCount: count
        avg {
          interestRate
        }
        stdev {
          interestRate
        }
        stdevp {
          interestRate
        }
      }
    }
  }
}
```

## Parent Filter by Nested Aggregate Criteria

```graphql
query {
  people(
    where: {
      ordersAggregate: {
        where: { status: { eq: ACTIVE } }
        having: { sum: { total: { gt: 1000 } } }
      }
    }
  ) {
    id
    name
  }
}
```

## Parent Filter by Nested Group Criteria

```graphql
query {
  customers(
    where: {
      ordersGroup: {
        by: [status]
        having: { count: { gte: 3 } }
      }
    }
  ) {
    id
    name
  }
}
```

## Parent Filter by Coupon Count

```graphql
query {
  securities(
    where: {
      details: {
        couponsAggregate: {
          having: { count: { gte: 3 } }
        }
      }
    }
  ) {
    id
    isin
  }
}
```

## Parent Filter by Date-Bounded Aggregate Total

```graphql
query {
  customers(
    where: {
      ordersAggregate: {
        where: { createdAt: { gte: "2026-02-17T00:00:00" } }
        having: { sum: { total: { gt: 1000 } } }
      }
    }
  ) {
    id
    name
  }
}
```

## GraphQL Aliases Remain the Alias Mechanism

```graphql
query {
  customers {
    salesByStatus: ordersGroup(by: [status]) {
      key {
        status
      }
      rowCount: count
      grossSales: sum {
        total
      }
    }
  }
}
```
