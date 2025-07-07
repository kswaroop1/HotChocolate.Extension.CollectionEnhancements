# Aggregation - Product Requirements Document

## Scope

This document defines the aggregation family of features:

- raw collection querying with `where`, `order`, `offset`, and `limit`
- one-row aggregation with `<field>Aggregate(...)`
- grouped aggregation with `<field>Group(...)`
- parent filtering based on aggregate criteria over nested collections

Shared product-level concerns such as registration, example domain, and the
split between aggregation and flattening are defined in `MISSION.md`.

## Executive Summary

Build a GraphQL-native aggregation surface that lets users ask SQL-shaped
questions over collection-valued fields without leaving the normal GraphQL
selection model.

## Vision Statement

"Let GraphQL users ask relational aggregate questions over any collection field
using familiar `where`, `by`, `having`, and grouped ordering semantics."

## Canonical Surface

### 1. Raw Collection Querying

Every eligible collection field gains:

- `where`
- `order`
- `offset`
- `limit`

Example:

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

### Field Eligibility and Response Shape

For v1, an eligible collection field is any field whose GraphQL element type is
an object type from the schema. Collections of scalars, enums, or strings are
out of scope unless separately documented.

The canonical query shape is direct list selection over the collection field
itself. The documented examples do not assume connection wrappers or an `items`
subfield.

### 2. Single Aggregate Row

For a collection field named `orders`, generate:

- `ordersAggregate(where:, having:)`
- the selected field returns zero or one post-`having` aggregate row
- if `having` eliminates that row, the field resolves to `null`

Example:

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

### 3. Grouped Aggregation

For a collection field named `orders`, generate:

- `ordersGroup(by:, where:, having:, order:, offset:, limit:)`

Each grouped row exposes:

- `key`
- aggregate operator fields directly on the row

Example:

```graphql
query {
  customers {
    ordersGroup(
      by: [status]
      having: { count: { gte: 2 } }
      order: [
        { key: { status: ASC } }
        { sum: { total: DESC } }
      ]
      limit: 5
    ) {
      key {
        status
      }
      count
      sum {
        total
      }
      stdev {
        total
      }
    }
  }
}
```

### Why `Aggregate` and `Group` Stay Separate

`Aggregate` and `Group` may share one internal planning model, but they should
remain separate public schema fields.

Reasons:

- `Aggregate` returns a single aggregate object
- `Group` returns a list of grouped rows with a required `key`
- GraphQL fields should not change their static result shape based on whether
  `by` is present
- forcing the non-grouped case through `Group` would either require a synthetic
  empty-key row or a list-of-one result shape for plain aggregation

Implementation guidance:

- it is valid to normalize `Aggregate` internally to the no-key grouped case
- that implementation detail should not leak into the public GraphQL surface

### 4. Parent Filtering by Aggregate Criteria

Example:

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

Business-shaped examples:

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

Grouped existential predicates are also supported:

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

## Aggregate Operator Vocabulary

### General Operators

- `count`
- `countDistinct`
- `sum`
- `avg`
- `min`
- `max`
- `stringAgg`
- `stringAggDistinct`

### Numeric-Only Operators

- `stdev`: sample standard deviation
- `stdevp`: population standard deviation
- `skew`: skewness
- `kurtosis`: kurtosis

Rules:

- `stdev`, `stdevp`, `skew`, and `kurtosis` are only valid for numeric fields
- `skew` and `kurtosis` are included because a one-pass implementation is
  possible using running moments
- if a provider cannot translate `skew` or `kurtosis`, the in-memory fallback
  must still preserve single-pass execution

## Canonical Aggregate SDL Shape

For a collection field named `orders`, the generated surface is:

```graphql
type Person {
  orders(
    where: OrderFilterInput
    order: [OrderSortInput!]
    offset: Int
    limit: Int
  ): [Order!]!

  ordersAggregate(
    where: OrderFilterInput
    having: OrdersAggregateHavingInput
  ): OrdersAggregateResult

  ordersGroup(
    by: [OrdersGroupByInput!]!
    where: OrderFilterInput
    having: OrdersGroupHavingInput
    order: [OrdersGroupOrderInput!]
    offset: Int
    limit: Int
  ): [OrdersGroupRow!]!
}

type OrdersAggregateResult {
  count(where: OrderFilterInput): Int!
  countDistinct: OrdersCountDistinctResult!
  sum: OrdersSumResult!
  avg: OrdersAvgResult!
  min: OrdersMinResult!
  max: OrdersMaxResult!
  stdev: OrdersStdevResult!
  stdevp: OrdersStdevpResult!
  skew: OrdersSkewResult!
  kurtosis: OrdersKurtosisResult!
  stringAgg(separator: String!, order: [OrderSortInput!]): OrdersStringAggResult!
  stringAggDistinct(separator: String!, order: [OrderSortInput!]): OrdersStringAggResult!
}

type OrdersGroupRow {
  key: OrdersGroupKey!
  count(where: OrderFilterInput): Int!
  countDistinct: OrdersCountDistinctResult!
  sum: OrdersSumResult!
  avg: OrdersAvgResult!
  min: OrdersMinResult!
  max: OrdersMaxResult!
  stdev: OrdersStdevResult!
  stdevp: OrdersStdevpResult!
  skew: OrdersSkewResult!
  kurtosis: OrdersKurtosisResult!
  stringAgg(separator: String!, order: [OrderSortInput!]): OrdersStringAggResult!
  stringAggDistinct(separator: String!, order: [OrderSortInput!]): OrdersStringAggResult!
}
```

Operator result-type rules:

- `count` is scalar and returns `Int!`.
- `countDistinct` exposes distinct counts for selectable scalar and enum members
  and returns `Int!` per projected member field.
- `sum` exposes numeric members only and preserves the normal HotChocolate
  scalar mapping of the underlying member type.
- `avg`, `stdev`, `stdevp`, `skew`, and `kurtosis` expose numeric members only
  and return the operator's numeric scalar result for each projected member.
- `min` and `max` expose comparable scalar, enum, date, and time members using
  the normal scalar mapping of the underlying member type.
- `stringAgg` and `stringAggDistinct` expose string members only and return
  `String` for each projected member field.
- The operator result types reuse the collection element member names directly,
  so examples such as `sum { total }`, `countDistinct { reference }`, and
  `stringAgg(...) { reference }` are canonical query shapes rather than
  illustrative pseudocode.

### Why Operator Projections Use Selection Sets

The canonical operator surface keeps GraphQL's normal field-selection model:

- `avg { total }`
- `countDistinct { reference }`
- `stringAgg(separator: ", ") { reference }`

This is intentional.

- `count` is the scalar exception because it naturally returns one number
- projection operators should scale from one projected member to several without
  changing syntax
- a shape such as `{ avg: total }` is not an aggregate projection surface; in
  GraphQL it reads as an alias on a field named `total`

Multi-member operator projections are therefore part of the intended design, not
an accidental side effect.

Example:

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

## HotChocolate Extensibility Fit

This feature family fits HotChocolate's extension model, but it is a custom
feature set rather than a thin wrapper over built-in filtering and sorting.

Implementation is expected to use:

- schema-time augmentation of eligible collection fields with generated sibling
  `Aggregate` and `Group` fields
- generated aggregate result types plus generated `having`, `group by`, and
  grouped-order input types
- custom filter/provider integration for nested parent predicates such as
  `ordersAggregate` and `ordersGroup` inside parent `where` inputs
- field middleware or equivalent planning logic that reads the GraphQL selection
  set and computes only the requested operators

This design stays within normal GraphQL rules because all public fields and
input types remain statically declared in the schema.

### Conditional Counting

`count` may accept an optional `where` predicate.

Example:

```graphql
query {
  customers {
    ordersAggregate {
      activeOrders: count(where: { status: { eq: ACTIVE } })
      cancelledOrders: count(where: { status: { eq: CANCELLED } })
    }
  }
}
```

## `having`

`having` is the post-aggregation predicate vocabulary.

Rules:

- `where` applies before grouping and aggregation
- `having` applies after aggregation
- `having` can be used with both `Aggregate` and `Group`
- `having` supports `and`, `or`, and `not`
- `having` uses aggregate-aware filter shapes matching the output operators
- if `having` rejects the logical row of `Aggregate`, the `Aggregate` field
  resolves to `null`
- if `having` rejects all grouped rows of `Group`, the `Group` field resolves to
  an empty list

Canonical input-shape rules:

- `OrdersAggregateHavingInput` and `OrdersGroupHavingInput` mirror the aggregate
  result shape.
- `count` uses the normal integer operation filter input.
- `countDistinct` uses nested integer operation filters keyed by projected
  member name.
- `sum`, `avg`, `min`, `max`, `stdev`, `stdevp`, `skew`, and `kurtosis` use
  nested operation filters keyed by the projected member name exposed on the
  corresponding operator result type.
- `stringAgg` and `stringAggDistinct` use string operation filters keyed by the
  projected member name.

Example:

```graphql
having: {
  and: [
    { count: { gte: 5 } }
    { stdev: { total: { lt: 50 } } }
    { kurtosis: { total: { lt: 10 } } }
  ]
}
```

## Grouped Ordering

Grouped ordering may target:

- grouping-key fields
- aggregate operator fields
- mixed key plus aggregate ordering

Example:

```graphql
order: [
  { key: { status: ASC } }
  { count: DESC }
  { stdev: { total: DESC } }
]
```

Canonical grouped-order rules:

- `OrdersGroupOrderInput` mirrors `OrdersGroupRow`.
- `key` uses a nested order input over the selected group key fields.
- `count` uses `SortEnumType`.
- `countDistinct`, `sum`, `avg`, `min`, `max`, `stdev`, `stdevp`, `skew`,
  `kurtosis`, `stringAgg`, and `stringAggDistinct` use nested order inputs keyed
  by the projected member names on the corresponding operator result type.

Grouped parent-filter semantics are existential. A parent matches an
`ordersGroup` predicate only if at least one grouped row remains after applying
`by`, `where`, and `having`.

## Performance Requirements

- aggregate planning must be selection-set-aware
- provider-backed execution should prefer one aggregate or grouped projection per
  field
- in-memory fallback must not do one pass per operator
- `stdev`, `stdevp`, `skew`, and `kurtosis` must preserve one-pass execution in
  fallback mode

## Success Criteria

- the language reads naturally to GraphQL users
- SQL users can infer `where`, `by`, `having`, and grouped ordering semantics
- `countDistinct`, `stringAgg`, `stdev`, and `stdevp` are first-class features
- `skew` and `kurtosis` are supported as long as the implementation remains
  single-pass
- all documented examples can be turned into integration tests over the shared
  example domain in `MISSION.md`
