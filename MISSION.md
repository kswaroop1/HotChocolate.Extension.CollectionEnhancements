# MISSION: HotChocolate Collection Enhancements

`MISSION.md` is the product-level canonical spec for this repository. It owns:

- the shared mission and design principles
- schema-level registration
- the shared example domain that all usage examples should be testable against
- the top-level split between the two feature families: aggregation and flattening

## Mission

Build a HotChocolate 15 extension that makes collection-valued fields behave
like first-class queryables while remaining natural to GraphQL users and easy to
reason about for SQL users.

## Feature Families

### 1. Aggregation Family

This family covers both raw collection querying and SQL-shaped aggregation:

- raw collection querying with `where`, `order`, `offset`, and `limit`
- one-row aggregation with `<field>Aggregate(where:, having:)`
- grouped aggregation with `<field>Group(by:, where:, having:, order:, offset:, limit:)`
- parent filtering based on aggregate conditions over nested collections

Canonical aggregate operators:

- `count`
- `countDistinct`
- `sum`
- `avg`
- `min`
- `max`
- `stdev`
- `stdevp`
- `stringAgg`
- `stringAggDistinct`

Advanced moment operators for numeric fields:

- `skew`
- `kurtosis`

`skew` and `kurtosis` are included because they can be implemented in a single
pass by maintaining the required running moments. Provider-backed translation is
preferred, but a one-pass in-memory fallback is acceptable.

### 2. Flatten Family

This family covers row expansion for export and tabular workflows.

The canonical surface is a generated sibling field family with explicit
`expand` sources:

- `<field>Flat(expand:, where:, order:, offset:, limit:, maxDepth:)`
- `<field>FlatAggregate(expand:, where:, having:)`
- `<field>FlatGroup(expand:, by:, where:, having:, order:, offset:, limit:)`

Semantics:

- any eligible collection field may gain `Flat`, `FlatAggregate`, and
  `FlatGroup` siblings while the original field remains unchanged
- one output row per reachable combination across the expanded sources
- selected outer fields are repeated on every emitted row
- if more than one `expand` source is supplied, the flat rowset uses
  cross-apply-style cartesian expansion relative to the same outer row
- generated names start from the terminal path segment and lengthen only when
  needed to stay unique
- flat rowsets are first-class collection surfaces and therefore reuse the
  shared rowset, aggregate, and grouped-query features
- root-level `Flat` and `FlatGroup` selections may additionally be serialized
  as CSV via `@export(format: CSV, separator:, includeHeader:, fileName:)`
- GraphQL aliases remain response-level renaming only; they do not replace the
  generated flat-column names in the schema
- flatten schema generation is host-type-scoped: generate one flat row type and
  field family per host type rather than one schema shape per expand
  combination

## Design Principles

- Prefer standard GraphQL mechanisms when they already solve the problem well.
- Prefer SQL terminology when it expresses relational behavior more clearly.
- Keep the language static and schema-friendly where possible.
- Use flatten-specific prefixes only for generated flat columns; use GraphQL
  aliases everywhere else.
- Require implementation strategies that push work into the provider and avoid
  repeated scans of the same underlying collection.
- Avoid combinatorial SDL growth; do not generate redundant ancestor-prefixed or
  path-combination-specific output shapes.
- Treat every documented example query as an intended integration test case.

## Canonical Registration Model

Registration is schema-level:

```csharp
services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddFiltering()
    .AddSorting()
    .AddProjections()
    .AddCollectionEnhancements();
```

Notes:

- There is no canonical `[UseCollectionEnhancements]` attribute.
- Existing HotChocolate middleware remains the way to opt into root-field
  filtering, sorting, projection, and paging.
- `AddCollectionEnhancements()` registers both feature families.
- `AddCollectionEnhancements()` is expected to use HotChocolate extensibility
  hooks such as schema generation, type extension, custom input/object types,
  convention/provider integration, validation, and field middleware or planning
  logic. These features are not expected to appear automatically from
  `AddFiltering()` / `AddSorting()` alone.

## Shared Semantics

- `where` is always pre-aggregation filtering.
- `having` is always post-aggregation filtering.
- `order` on grouped results may target either grouping-key fields or aggregate
  operator fields.
- `offset` and `limit` apply after ordering.
- `Aggregate` and `Group` remain separate public fields because they have
  different static GraphQL result shapes. `Aggregate` returns one aggregate
  object, while `Group` returns grouped rows with `key`. Implementations may
  plan `Aggregate` internally as the no-key grouped case, but the schema should
  keep the two public surfaces distinct.
- The same distinction applies to `FlatAggregate` and `FlatGroup`.
- `Aggregate` is the zero-or-one post-`having` form. If the single aggregate row
  is eliminated by `having`, the field resolves to `null`.
- `Group` is the zero-or-more post-`having` form. If no grouped rows remain, the
  field resolves to an empty list.
- Documented GraphQL examples are normative acceptance targets. They should
  compile and execute as written against the shared example domain without
  conceptual translation into a different query shape.
- Unless a feature explicitly documents a connection-style wrapper, collection
  fields in examples are queried directly on the field itself rather than
  through an `items` wrapper.
- Flat `expand` sources are resolved segment-by-segment against the current
  type.
  Repeated names at different levels are not ambiguous because each path segment
  is interpreted in the context of the preceding segment's type. Path strings are
  anchored at the selected field result type rather than resolved by global name
  lookup.
- Flat-generated fields are schema fields on the explicit flat row type. The
  execution phase computes row values, but the field names and types must
  already be present on that static GraphQL output type.
- `<field>Flat(expand:)`, `<field>FlatAggregate(expand:)`, and
  `<field>FlatGroup(expand:)` may receive literal or variable `expand` values.
  Requests using variables must be validated after argument coercion and before
  row expansion or resolver execution.
- `<field>Flat(where:, order:, offset:, limit:)` acts on the flat rowset
  after expansion. `offset` and `limit` apply after flattened-row ordering.
- `<field>Flat(where:)`, `<field>FlatAggregate(where:/having:)`, and
  `<field>FlatGroup(by:/where:/having:/order:)` use the same schema field names
  as the host-scoped flat row family, including generated flat field names such
  as `couponPaymentDate`.
- Selecting generated flat fields that do not belong to the requested `expand`
  sources is invalid and should be rejected by validation.
- Referencing generated flat fields in `where`, `by`, `having`, or `order` that
  do not belong to the requested `expand` sources is equally invalid and must
  be rejected before row expansion.
- For multi-source flattening, if any requested `expand` source yields no
  terminal rows for a given outer row, that outer row contributes zero emitted
  rows because the result uses cross-apply-style cartesian expansion over the
  requested sources.
- `@export` is a root-output directive, not a schema-shape directive. It may
  appear only on a single root `<field>Flat` or `<field>FlatGroup` selection in
  a query operation.
- When `@export(format: CSV)` is present, the selected root field is serialized
  as `text/csv` instead of the normal GraphQL JSON response body.
- CSV export uses the selected scalar and enum leaf fields as columns. For
  `FlatGroup`, this includes leaf members selected under `key` and aggregate
  operator objects.
- CSV column order follows leaf-selection order. An explicit alias on the leaf
  selection becomes the header name; otherwise the header is derived from the
  leaf response path using `_` separators.
- `separator` must be a single-character string after coercion. Exported header
  names must be unique after alias and response-path resolution.
- All examples in the PRDs and usage example docs should become executable test
  cases over the example domain below.

## Example Domain

```csharp
// financial securities domain
public record Coupon(DateOnly ObservationDate, DateOnly PaymentDate, decimal? InterestRate);
public record Call(DateOnly ObservationDate, DateOnly CallDate, bool IsCalled);
public record Underlying(int Id, string Ric, string Currency, decimal StrikePrice);
public record SecurityDetails(Underlying[] Underlyings, Coupon[] Coupons, Call[] Calls);
public record Security(int Id, string Isin, string Currency, decimal Price, decimal Notional,
    DateOnly StrikeDate, DateOnly ExpirationDate, SecurityDetails Details);
public interface ISecurityService { IQueryable<Security> GetSecurities(); }

// customer orders domain
public record Tag(int Id, string TagName, string Category);
public record OrderItem(int Id, string ProductName, int Quantity, Tag[] Tags);
public enum OrderStatus { Pending, Active, Cancelled, Completed }
public record Order(int Id, DateTime CreatedAt, decimal Total, OrderStatus Status, string Reference, OrderItem[] Items);
public record Customer(int Id, string Name, Order[] Orders);
public interface ICustomerService { IQueryable<Customer> GetCustomers(); }

// person social media post domain
public record Comment(int Id, string Body, string Author);
public record Post(int Id, string Title, Comment[] Comments);
public record Person(int Id, string Name, Post[] Posts, Order[] Orders);
public interface IPersonService { IQueryable<Person> GetPeople(); }

public sealed class Query
{
    [UseFiltering]
    [UseSorting]
    [UseProjection]
    public IQueryable<Security> GetSecurities([Service] ISecurityService service)
        => service.GetSecurities();

    [UseFiltering]
    [UseSorting]
    [UseProjection]
    public IQueryable<Customer> GetCustomers([Service] ICustomerService service)
        => service.GetCustomers();

    [UseFiltering]
    [UseSorting]
    [UseProjection]
    public IQueryable<Person> GetPeople([Service] IPersonService service)
        => service.GetPeople();
}
```

## Canonical Capability Examples

### Aggregation Example

```graphql
query {
  securities(where: { id: { eq: 1 } }) {
    id
    details {
      couponsAggregate(where: { interestRate: { gt: 0.05 } }) {
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

### Flat Cross-Apply Example

```graphql
query {
  securityRows: securitiesFlat(
    expand: [
      "details.underlyings"
      "details.coupons"
      "details.calls"
    ]
  ) {
    id
    isin
    underlyingRic
    underlyingCurrency
    couponObservationDate
    couponPaymentDate
    couponInterestRate
    callObservationDate
    callCallDate
    callIsCalled
  }
}
```

The flat example is intentionally row-oriented: one output row per reachable
combination of underlying, coupon, and call for a given security, with the outer
security columns repeated on each emitted row.

### Nested Local Flat Example

```graphql
query {
  people {
    id
    name
    orderTagRows: ordersFlat(
      expand: ["items.tags"]
    ) {
      id
      total
      status
      tagTagName
      tagCategory
    }
  }
}
```

This example keeps the outer `people` result shape and uses the generated flat
sibling only within the nested `orders` context. The host type for the flat
rowset is `Order`, so
there is no need to generate redundant `ordersTag...` variants.

### Root Flat CSV Export Example

```graphql
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
```

This keeps the GraphQL field shape unchanged and changes only the root response
serialization. The CSV headers are `securityId`, `isin`, `paymentDate`, and
`rate`.
