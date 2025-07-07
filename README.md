# HotChocolate.Extension.CollectionEnhancements

Specification-first workspace for a HotChocolate 15 extension that focuses on
two feature families:

- aggregation and collection querying
- flattening via generated flat-row sibling fields

## Canonical Docs

- `MISSION.md`: product-level mission, shared language rules, example domain
- `AGGREGATION_PRD.md`: aggregation-family feature spec
- `FLATTEN_PRD.md`: flat-row-family feature spec
- `TASKS.md`: top-level project tracker

## Detailed Implementation Trackers

- `AGGREGATION_IMPLEMENTATION_TODO.md`
- `AGGREGATION_IMPLEMENTATION_NOTE.md`
- `FLATTEN_IMPLEMENTATION_TODO.md`
- `EXAMPLE_TEST_MATRIX.md`

## HotChocolate Fit

The intended implementation model is custom HotChocolate extensibility work, not
just built-in filtering and sorting:

- schema-time generation of sibling fields, result types, and input types
- type extension or dynamic schema hooks for host-type field augmentation
- custom filter/order providers where aggregate-aware predicates are needed
- field middleware or planning logic that executes selection-set-aware aggregate,
  group, and flat-row behavior

## HotChocolate Reference Docs

- `HOTCHOCOLATE_ARCHITECTURE_GUIDE.md`
- `HOTCHOCOLATE_ADVANCED_PATTERNS.md`
