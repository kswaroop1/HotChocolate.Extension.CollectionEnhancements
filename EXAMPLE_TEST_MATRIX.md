# Example Test Matrix

This document deduplicates the documented GraphQL examples into an executable
acceptance-test canon.

Rules:

- The docs are canonical.
- Queries should run exactly as written.
- Direct collection-field selection is canonical unless a feature explicitly
  documents a different wrapper.
- Repeated examples across `MISSION.md`, PRDs, and usage docs map to one
  canonical behavior test with multiple source references.

## Aggregation Canon

### A01. Raw collection querying over a nested collection

- Sources: `AGGREGATION_PRD.md`, `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: `people { orders(where:, order:, offset:, limit:) { ... } }`
- Fixtures: at least one person with enough orders to exercise filtering,
  sorting, and pagination together
- Minimum assertions: filtered rows only, sort order preserved, `offset` and
  `limit` applied after ordering

### A02. Single aggregate row with pre-aggregation filtering

- Sources: `MISSION.md`, `AGGREGATION_PRD.md`, `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: `<field>Aggregate(where:)`
- Fixtures: at least one collection with rows both inside and outside the
  `where` predicate
- Minimum assertions: `count`, `avg`, `stdev`, and `stdevp` are computed only
  over the filtered rows

### A03. Higher moments on numeric fields

- Sources: `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: `skew` and `kurtosis` over `orders.total`
- Fixtures: at least four numeric values producing deterministic skewness and
  kurtosis
- Minimum assertions: schema allows the operators on numeric members and returns
  stable numeric results

### A04. Distinct count and string aggregation

- Sources: `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: `countDistinct`, `stringAgg`, `stringAggDistinct`
- Fixtures: repeated `reference` values and a deterministic sort order
- Minimum assertions: `countDistinct { reference }` returns the distinct count,
  `stringAgg` honors `separator` and `order`, and `stringAggDistinct` removes
  duplicates before concatenation

### A05. Multi-member operator projection

- Sources: `AGGREGATION_PRD.md`, `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: one operator projecting more than one member field
- Fixtures: root security data with distinct `currency`, `isin`, `price`, and
  `expirationDate` values
- Minimum assertions: selection-set-based operator projection works across
  multiple members and preserves the documented query shape

### A06. Grouped aggregation

- Sources: `AGGREGATION_PRD.md`, `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: `<field>Group(by:, order:)`
- Fixtures: multiple groups with distinct aggregate values
- Minimum assertions: grouped rows expose `key` and operator fields, group order
  can mix key ordering and aggregate ordering

### A07. Grouped aggregation with `having`

- Sources: `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: grouped `having` with boolean composition
- Fixtures: groups that both pass and fail `count` and `stdev` predicates
- Minimum assertions: `having` is applied post-aggregation and boolean
  composition behaves correctly

### A08. Conditional count

- Sources: `AGGREGATION_PRD.md`, `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: `count(where: ...)`
- Fixtures: rows across multiple statuses
- Minimum assertions: each conditional count uses its own predicate and returns
  an `Int`

### A09. Aggregation over nested security collections

- Sources: `MISSION.md`, `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: `securities { details { couponsAggregate { ... } } }`
- Fixtures: at least one security with multiple coupons and nullable rates
- Minimum assertions: nested collection aggregation is available and numeric
  operators project `interestRate` correctly

### A10. Parent filtering by nested aggregate criteria

- Sources: `AGGREGATION_PRD.md`, `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: parent `where` using `<field>Aggregate`
- Fixtures: parents both above and below the aggregate threshold
- Minimum assertions: `where` inside the aggregate filter is pre-aggregation,
  `having` is post-aggregation, and only matching parents remain

### A11. Parent filtering by grouped criteria

- Sources: `AGGREGATION_PRD.md`, `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: parent `where` using `<field>Group`
- Fixtures: parents with zero, one, and multiple qualifying groups
- Minimum assertions: semantics are existential and depend on grouped rows after
  `by`, `where`, and `having`

### A12. Parent filtering by coupon count

- Sources: `AGGREGATION_PRD.md`, `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: `securities(where: { details: { couponsAggregate: { having: { count: { gte: 3 } } } } })`
- Fixtures: securities on both sides of the coupon-count threshold
- Minimum assertions: nested object filtering composes with aggregate filtering
  and returns only securities with at least three coupons

### A13. Parent filtering by date-bounded aggregate total

- Sources: `AGGREGATION_PRD.md`, `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: customer `ordersAggregate` filter with both `where` and `having`
- Fixtures: customers with order totals both above and below the threshold since
  `2026-02-17T00:00:00`
- Minimum assertions: date-bounded `where` is applied before aggregation and the
  aggregate `having` threshold is applied after aggregation

### A14. Alias behavior for grouped aggregates

- Sources: `AGGREGATION_USAGE_EXAMPLES.md`
- Query shape: aliases on the group field and operator fields
- Fixtures: any grouped dataset
- Minimum assertions: aliases rename the response only and do not imply renamed
  schema fields

## Flatten Canon

### F01. Single-source flat field with derived prefix

- Sources: `FLATTEN_PRD.md`, `FLATTEN_USAGE_EXAMPLES.md`
- Query shape: `securitiesFlat(expand: ["details.coupons"])`
- Fixtures: at least one security with multiple coupons
- Minimum assertions: one row per reachable coupon and generated field names use
  the derived `coupon` prefix

### F02. Multi-source cross-apply flat field

- Sources: `MISSION.md`, `FLATTEN_PRD.md`, `FLATTEN_USAGE_EXAMPLES.md`
- Query shape: `securitiesFlat(expand: ["details.underlyings", "details.coupons", "details.calls"])`
- Fixtures: at least one security with multiple values in each nested collection
- Minimum assertions: row count equals the cross-product cardinality and outer
  fields such as `id` and `isin` repeat on every emitted row

### F03. Nested local flat field

- Sources: `MISSION.md`, `FLATTEN_PRD.md`, `FLATTEN_USAGE_EXAMPLES.md`
- Query shape: `people { orderTagRows: ordersFlat(expand: ["items.tags"]) { ... } }`
- Fixtures: at least one person with orders, items, and tags
- Minimum assertions: the outer `people` shape remains intact and flat-row
  querying is scoped to the nested `orders` field

### F04. Flat field with filter, order, and slicing

- Sources: `FLATTEN_PRD.md`, `FLATTEN_USAGE_EXAMPLES.md`
- Query shape: `<field>Flat(expand:, where:, order:, offset:, limit:)`
- Fixtures: coupon rows both before and after the filter threshold
- Minimum assertions: `where` uses generated flat field names and filters after
  expansion but before final emission, ordering applies before `offset` and
  `limit`, and slicing occurs on flat rows

### F05. Flat aggregate over expanded rows

- Sources: `FLATTEN_PRD.md`, `FLATTEN_USAGE_EXAMPLES.md`
- Query shape: `<field>FlatAggregate(expand:, where:, having:)`
- Fixtures: at least one expanded rowset with numeric flat members
- Minimum assertions: flat aggregates operate over the expanded rowset, `where`
  is pre-aggregation, `having` is post-aggregation, and the result follows the
  normal nullable aggregate semantics

### F06. Flat grouped aggregation

- Sources: `FLATTEN_PRD.md`, `FLATTEN_USAGE_EXAMPLES.md`
- Query shape: `<field>FlatGroup(expand:, by:, where:, having:, order:, offset:, limit:)`
- Fixtures: at least one expanded rowset with multiple distinct grouping values
- Minimum assertions: grouped rows expose `key`, aggregate operators work over
  the flat row members, and grouped ordering/slicing semantics match the
  non-flat aggregate family

### F07. Generated-field alias behavior

- Sources: `FLATTEN_USAGE_EXAMPLES.md`
- Query shape: aliasing `couponPaymentDate` to `payDate`
- Fixtures: any coupon data
- Minimum assertions: the response uses the alias while the schema field name
  remains `couponPaymentDate`

### F08. Result-field alias behavior

- Sources: `FLATTEN_USAGE_EXAMPLES.md`
- Query shape: aliasing the flat result field itself, for example
  `exportRows: securitiesFlat(...)`
- Fixtures: any coupon data
- Minimum assertions: aliasing the result field does not affect generated column
  naming or flat-row semantics

### F09. Root flat CSV export

- Sources: `MISSION.md`, `FLATTEN_PRD.md`, `FLATTEN_USAGE_EXAMPLES.md`
- Query shape: root `<field>Flat @export(format: CSV, separator:)`
- Fixtures: coupon rows with deterministic ordering
- Minimum assertions: response content type is `text/csv`, the separator is
  honored, selected leaf aliases become header names, and data rows follow the
  same rowset semantics as the underlying flat field

### F10. Root flat-group CSV export

- Sources: `FLATTEN_PRD.md`, `FLATTEN_USAGE_EXAMPLES.md`
- Query shape: root `<field>FlatGroup @export(format: CSV, separator:)`
- Fixtures: grouped coupon data with deterministic key order
- Minimum assertions: response content type is `text/csv`, selected scalar leaf
  members under `key` and aggregate operator objects become columns, leaf alias
  headers are honored, and grouped rows preserve the underlying grouped
  semantics

## Required Negative Coverage

### N01. Invalid numeric aggregate usage

- Sources: `AGGREGATION_PRD.md`
- Query shape: applying `stdev`, `stdevp`, `skew`, or `kurtosis` to a
  non-numeric field
- Minimum assertions: the request is rejected before execution

### N02. Invalid flat expand path traversal

- Sources: `FLATTEN_PRD.md`
- Query shape: invalid or non-existent path segments
- Minimum assertions: the request is rejected before row expansion

### N03. Invalid generated-field selection for the requested `expand`

- Sources: `MISSION.md`, `FLATTEN_PRD.md`
- Query shape: selecting generated fields not implied by `expand`
- Minimum assertions: the request is rejected before row expansion for both
  literal and variable `expand` requests

### N04. Invalid flat `where` field usage for the requested `expand`

- Sources: `MISSION.md`, `FLATTEN_PRD.md`
- Query shape: referencing generated fields in `where` that are not implied by
  `expand`
- Minimum assertions: the request is rejected before row expansion for both
  literal and variable `expand` requests

### N05. Invalid flat aggregate or group field usage for the requested `expand`

- Sources: `MISSION.md`, `FLATTEN_PRD.md`
- Query shape: projecting, grouping, ordering, or filtering on generated flat
  fields that are not implied by `expand`
- Minimum assertions: the request is rejected before row expansion for both
  literal and variable `expand` requests

### N06. Invalid export target or nesting

- Sources: `MISSION.md`, `FLATTEN_PRD.md`
- Query shape: `@export` on nested flat fields, on non-flat fields, or on root
  `FlatAggregate`
- Minimum assertions: the request is rejected before execution and does not
  enter CSV serialization

### N07. Invalid export operation shape

- Sources: `MISSION.md`, `FLATTEN_PRD.md`
- Query shape: `@export` used with multiple root fields in the same operation
- Minimum assertions: the request is rejected before execution because export
  requires exactly one exported root field

### N08. Invalid export separator or duplicate headers

- Sources: `MISSION.md`, `FLATTEN_PRD.md`
- Query shape: `@export(separator:)` with a value that does not coerce to a
  single character, or leaf selections that resolve to duplicate CSV headers
- Minimum assertions: the request is rejected before execution and the error
  points at the export configuration
