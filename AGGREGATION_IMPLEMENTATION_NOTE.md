# Aggregation Implementation Note

This document captures implementation guidance that complements
`AGGREGATION_PRD.md`. It does not replace the canonical product spec.

## 1. Keep `Aggregate` and `Group` Separate Publicly

The public GraphQL surface should keep:

- `<field>Aggregate(where:, having:)`
- `<field>Group(by:, where:, having:, order:, offset:, limit:)`

Rationale:

- `Aggregate` returns one aggregate object
- `Group` returns grouped rows with a required `key`
- GraphQL fields should not vary their static result shape based on whether `by`
  was supplied
- forcing plain aggregation through `Group` would introduce awkward list-of-one
  or empty-key semantics into the public API

Implementation guidance:

- normalize `Aggregate` internally to the no-key grouped case if that makes the
  planner simpler
- keep that normalization internal and do not expose grouped-row artifacts to
  clients of `Aggregate`

## 2. One Planner, Two Result Shapes

A shared internal planning model is encouraged.

The common plan should capture:

- source collection
- pre-aggregation `where`
- optional grouping keys
- requested aggregate operators from the GraphQL selection set
- post-aggregation `having`
- grouped ordering and slicing

The result shaper should then emit either:

- `null` or a single aggregate object for `Aggregate`, depending on whether
  post-aggregation `having` keeps the logical row
- a list of grouped rows with `key` for `Group`

## 3. Operator Projections Are Selection-Set Driven

Projection operators should follow the GraphQL selection model:

- `sum { total }`
- `avg { total }`
- `countDistinct { reference }`
- `stringAgg(separator: ", ") { reference }`

`count` is the scalar exception because it naturally returns one number.

Do not redesign projection operators into map-like syntax such as
`{ avg: total }`. In GraphQL that shape reads as alias syntax rather than a
distinct aggregate projection model.

## 4. Parent Filtering Should Reuse the Same Planner

Parent filters over nested collection aggregates should compile through the same
core planning model used for selected aggregate fields.

Examples the implementation should support cleanly:

- securities with at least three coupons
- customers whose order total since `2026-02-17T00:00:00` exceeds `1000`

The nested filter path is part of the surface API. The aggregate computation and
predicate translation should still reuse the same aggregation planner.

## 5. Flatten Relationship

Generated flat rowsets should be treated as first-class collection surfaces.

They should therefore reuse the same collection-enhancement rules as any other
eligible collection field:

- raw `where`, `order`, `offset`, and `limit`
- sibling `FlatAggregate(where:, having:)`
- sibling `FlatGroup(by:, where:, having:, order:, offset:, limit:)`

The original collection field still remains unchanged. The flattening behavior
belongs to the generated flat sibling field family, not to hidden runtime shape
changes on the original field.
