# Flat Row Fields - Product Requirements Document

## Scope

This document defines the flatten family of features.

Shared product-level concerns such as registration, example domain, and the
split between aggregation and flattening are defined in `MISSION.md`.

## Executive Summary

Build generated sibling GraphQL fields that expand one or more nested collection
sources into explicit flat, tabular rowsets. The feature family must support:

- single-source flattening
- multi-level flattening along one source
- multi-source cross-apply flattening across sibling nested collections
- shared rowset, aggregate, and grouped-query operators over the flat output
- optional CSV export for root-level flat and grouped-flat rowsets
- stable generated column names without generating redundant ancestor-prefixed
  variants or per-expand-combination types

## Vision Statement

"Make deeply nested GraphQL collections exportable as flat rows without losing
outer-row context."

## Canonical Surface

### 1. Flat Row Querying

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
    couponPaymentDate
    callCallDate
  }
}
```

For a collection field named `orders`, generate:

- `ordersFlat(expand:, where:, order:, offset:, limit:, maxDepth:)`

### 2. Flat Aggregate Row

For a collection field named `orders`, generate:

- `ordersFlatAggregate(expand:, where:, having:)`

### 3. Flat Grouped Aggregation

For a collection field named `orders`, generate:

- `ordersFlatGroup(expand:, by:, where:, having:, order:, offset:, limit:)`

### 4. Root CSV Export

CSV export is a root-output concern layered on top of root-level flat rowsets.

Canonical directive surface:

```graphql
directive @export(
  format: ExportFormat! = CSV
  separator: String = ","
  includeHeader: Boolean = true
  fileName: String
) on FIELD

enum ExportFormat {
  CSV
}
```

Rules:

- `@export` may appear only on a single root selection in a query operation
- the target root field must be `<field>Flat` or `<field>FlatGroup>`
- v1 supports `format: CSV` only
- `separator` must coerce to a single-character string
- when `@export` is present, the HTTP response body is `text/csv` rather than
  the normal GraphQL JSON body
- validation failures still use the normal GraphQL error response path because
  export validation must complete before row enumeration begins

### Arguments

Canonical flat-field arguments:

- `expand: [String!]!`
- `where: <optional filter input over the flat rowset>`
- `order: <optional order input over the flat rowset>`
- `offset: Int`
- `limit: Int`
- `maxDepth: Int` if defensive depth limits are needed

Rules:

- each entry in `expand` is a dot-separated path string resolved relative to the
  selected field result type
- the server derives a minimal unique generated-column prefix from the path,
  starting with the terminal segment and lengthening only when needed
- GraphQL aliases remain response-level renaming only; they do not replace
  generated schema field names
- `expand` may be supplied as literals or variables

Request-shape rules:

- generated flat fields remain schema fields on the explicit flat row type even
  when they are not valid for a particular `expand` request
- selected generated fields and fields referenced inside `where` use the same
  schema field names
- compatibility between the requested `expand`, the selected generated fields,
  and the fields referenced in `where` must be checked after argument coercion
  and before row expansion
- flat aggregate and flat group selections, `by`, `having`, and grouped `order`
  must satisfy that same `expand` compatibility rule
- literal `expand` requests may be rejected during validation or planning;
  variable `expand` requests must be rejected during request validation or
  execution planning before resolver execution
- when `@export` is present, the export column set is derived from the selected
  scalar and enum leaf fields after normal GraphQL validation succeeds

## Problem Statement

Without flattening, clients often have to read deeply nested arrays and then
manually denormalize them into rows for CSV, Excel, and tabular UI workflows.

The feature's real behavior is row expansion, not cosmetic renaming.

For a single source, emit one output row per reachable terminal inner row and
repeat the selected outer fields on every emitted row.

For multiple sources, emit one output row per reachable combination across
those sources relative to the same outer row. This is the cross-apply-style
cartesian behavior required for cases such as flattening `underlyings`,
`coupons`, and `calls` together for a single security.

A naive schema strategy would explode by generating a distinct type or field
family for every possible expand combination and ancestor-path variant. That is
explicitly not the intended design.

## Functional Requirements

### 1. Multi-Source Flattening

- support one or more `expand` sources in the same flat field call
- treat a single-source flat query as the degenerate case of the same model
- for multiple sources, produce the cross-apply-style cartesian combination of
  the terminal rowsets per outer row
- if any requested source yields no terminal rows for an outer row, that outer
  row contributes zero flat rows because the cartesian combination with an
  empty set is empty

### 2. Multi-Level Flattening

- support paths such as `details.coupons`
- support paths such as `orders.items.tags`
- if a path crosses multiple collection segments, the emitted rows correspond to
  the chained traversal through those segments

### 3. Host-Type Scoping and Schema Strategy

Flatten is host-type-scoped.

- flatten may be generated for any eligible collection-valued field, not only
  root fields
- the original collection field remains unchanged
- generate one flat row type and field family per host type, not one schema
  shape per expand combination
- do not generate redundant ancestor-prefixed duplicates such as both `zA` and
  `yzA` on the same host type unless a longer derived prefix is required for
  uniqueness
- values are produced at execution time, but the generated flat fields and
  their types must already exist on the explicit flat row type in the schema
- validate selected generated fields against the requested `expand`

Canonical SDL shape:

```graphql
type Person {
  ordersFlat(
    expand: [String!]!
    where: OrderFlatRowFilterInput
    order: [OrderFlatRowSortInput!]
    offset: Int
    limit: Int
    maxDepth: Int
  ): [OrderFlatRow!]!

  ordersFlatAggregate(
    expand: [String!]!
    where: OrderFlatRowFilterInput
    having: OrderFlatAggregateHavingInput
  ): OrderFlatAggregateResult

  ordersFlatGroup(
    expand: [String!]!
    by: [OrderFlatGroupByInput!]!
    where: OrderFlatRowFilterInput
    having: OrderFlatGroupHavingInput
    order: [OrderFlatGroupOrderInput!]
    offset: Int
    limit: Int
  ): [OrderFlatGroupRow!]!
}
```

### 4. Path Resolution and Repeated Names

Path resolution is structural and contextual.

- each segment is resolved against the type reached by the previous segment
- paths are anchored at the selected field result type rather than resolved by
  global name lookup
- repeated names at different levels are allowed
- the path does not become ambiguous just because a later segment repeats a name
  that appeared higher in the object graph
- a path such as `node.children.nodeEvents` is valid if each segment exists on
  the type reached by the prior segment

### 5. Context Preservation

The flat row preserves selected outer values.

Example intent:

- flattening security details should still allow `id` and `isin` on every row
- flattening a person's nested `orders` selection should still allow `id`,
  `total`, and `status` on every emitted order-tag row

### 6. Generated Column Naming

When flattening introduces fields from nested paths, the flat row needs stable
column names.

Canonical rule:

- generated flat fields are named as `<derivedPrefix><PascalCase(fieldName)>`
- the default derived prefix starts from the terminal path segment
- if that prefix would collide, extend it using the minimal unique path suffix
- GraphQL aliases may still rename the selected generated field in the response

Examples:

- `couponPaymentDate`
- `callObservationDate`
- `billingAddressLine1` and `shippingAddressLine1` when two paths share the same
  terminal segment `address`

This prefixing mechanism is not a replacement for GraphQL aliases. It is the
column-generation rule for flat path data.

### 7. Rowset, Aggregate, and Group Integration

`Flat`, `FlatAggregate`, and `FlatGroup` are first-class collection
enhancement surfaces over the expanded flat rowset.

Canonical operator-shape rules:

- `Flat` uses `where`, `order`, `offset`, and `limit` over the expanded flat
  rowset
- `FlatAggregate` uses `where` and `having` over the expanded flat rowset and
  follows the normal zero-or-one aggregate semantics
- `FlatGroup` uses `by`, `where`, `having`, `order`, `offset`, and `limit` over
  the expanded flat rowset and follows the normal zero-or-more grouped-row
  semantics
- the `where` vocabulary is defined over the explicit flat row type
- generated flat fields are addressed by the same names used in selection
  sets, such as `couponPaymentDate`, `callCallDate`, or `tagCategory`
- outer repeated fields that exist on that same flat row type may also
  participate in `where`
- referencing a generated field in `where` that is not implied by the requested
  `expand` sources is invalid
- `by`, `having`, and grouped `order` may target any outer or generated flat
  row field that is valid for the requested `expand`
- `offset` and `limit` apply after ordering
- flat fields should reuse the same filter, sort, aggregate, and grouped-query
  conventions as other eligible collection fields where practical

### 8. Root CSV Export

`@export` is the canonical CSV surface for root-level flat rowsets.

Scope rules:

- `@export` applies only to root `<field>Flat` and root `<field>FlatGroup>`
- nested uses such as `people { ordersFlat(...) @export { ... } }` are invalid
- an operation using `@export` must select exactly one root field
- root `<field>FlatAggregate` is not part of the v1 export surface

Column rules:

- exported columns are the selected scalar and enum leaf fields in document
  order
- root `<field>Flat` selections typically export flat row members directly
- root `<field>FlatGroup` selections export leaf members chosen under `key`,
  `count`, and aggregate operator objects such as `avg`, `min`, or `max`
- object containers such as `key` and `avg` are traversal nodes only; they do
  not become CSV columns by themselves

Header rules:

- if a scalar or enum leaf selection has an explicit alias, that alias becomes
  the CSV header
- otherwise the header is derived from the leaf response path joined with `_`
- examples:
  - `couponPaymentDate` -> `couponPaymentDate`
  - `key { couponPaymentDate }` -> `key_couponPaymentDate`
  - `avg { avgRate: couponInterestRate }` -> `avgRate`
- resulting header names must be unique after alias and response-path
  resolution

Serialization rules:

- CSV export uses GraphQL scalar serialization first and then applies CSV
  escaping and quoting
- `separator` controls field separation only; it does not affect GraphQL field
  names or aliases
- `includeHeader: true` emits one header row before data rows
- `fileName`, when supplied, is used for the HTTP `Content-Disposition`
  filename hint
- export should stream rows where practical rather than materializing the full
  file in memory

## HotChocolate Extensibility Fit

This feature family fits HotChocolate's schema-generation, type-extension,
validation, provider, and middleware hooks while staying aligned with
GraphQL's static type system.

Implementation is expected to use:

- schema-time generation of sibling flat fields and explicit flat row types
- custom validation after argument coercion to check `expand`,
  generated-field compatibility, and flat aggregate/group field usage
- field middleware or equivalent planning logic that performs row expansion and
  rowset shaping
- custom filter/order/group provider integration where aggregate-aware flat
  predicates are needed
- a root-output directive plus HTTP result-serialization hook for CSV export

This design is cleaner than a directive-led shape change because the original
collection field keeps its original GraphQL type and the flat field family has
its own explicit static types. `@export` is still appropriate because it changes
only the root response serialization rather than the schema shape of the target
field.

### 9. Type Safety and Validation

- validate `expand` paths against the schema
- validate path traversal across collection and object boundaries
- preserve nullability, scalar, and enum semantics on generated fields
- reject generated-field selections that are incompatible with the requested
  `expand`
- reject `where`, `by`, `having`, and grouped `order` clauses that reference
  generated fields incompatible with the requested `expand`
- perform all path/field compatibility checks before row expansion and before
  resolver execution begins

## Canonical Examples

### Single-Source Flat Query with Derived Prefix

```graphql
query {
  couponRows: securitiesFlat(
    expand: ["details.coupons"]
  ) {
    id
    isin
    couponObservationDate
    couponPaymentDate
    couponInterestRate
  }
}
```

### Multi-Source Cross-Apply Flat Query

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

### Nested Local Flat Query

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

### Flat Query with Filter, Order, and Slicing

```graphql
query {
  activeCouponRows: securitiesFlat(
    expand: ["details.coupons"]
    where: { couponPaymentDate: { gt: "2026-01-01" } }
    order: [{ couponPaymentDate: DESC }]
    offset: 5
    limit: 10
  ) {
    id
    isin
    couponPaymentDate
    couponInterestRate
  }
}
```

### Flat Aggregate

```graphql
query {
  securitiesFlatAggregate(
    expand: ["details.coupons"]
    where: { couponInterestRate: { gt: 0.03 } }
  ) {
    count
    avg {
      couponInterestRate
    }
    max {
      couponPaymentDate
    }
  }
}
```

### Flat Grouped Aggregation

```graphql
query {
  securitiesFlatGroup(
    expand: ["details.coupons"]
    by: [couponPaymentDate]
    order: [{ key: { couponPaymentDate: ASC } }]
  ) {
    key {
      couponPaymentDate
    }
    count
    avg {
      couponInterestRate
    }
  }
}
```

### Root Flat CSV Export

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

Expected column order:

- `securityId`
- `isin`
- `paymentDate`
- `rate`

### Root Flat Group CSV Export

```graphql
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
```

Expected column order:

- `paymentDate`
- `groupCount`
- `avgRate`

## Performance Requirements

- prefer provider-backed row expansion where feasible
- avoid repeated traversal of the same path
- evaluate multi-source flattening as one planned row-expansion step per field
- keep cross-apply semantics explicit rather than accidental
- keep schema growth proportional to host types and reachable flattenable leaf
  fields rather than expand-combination count

## Success Criteria

- flat sibling fields can emit flat rows for CSV and spreadsheet use
- multi-source cross-apply semantics are supported explicitly
- nested local flat fields keep the outer response shape intact
- repeated outer values are preserved on each row
- flat fields reuse the shared rowset, aggregate, and grouped-query features
- root-level flat and grouped-flat rowsets can be exported directly as CSV
- generated flat-column names remain deterministic and collision-safe
- the schema strategy avoids redundant ancestor-prefixed and
  expand-combination-specific type explosion
- all documented examples can become integration tests over the shared example
  domain in `MISSION.md`
