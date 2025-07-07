# Flat Row Fields - Implementation TODO

## HotChocolate Mapping

- [ ] Generate sibling `<field>Flat`, `<field>FlatAggregate`, and
  `<field>FlatGroup` fields in code-first HotChocolate.
- [ ] Generate host-type-scoped flat row types and related input/result types
  from the C# model without handwritten SDL.
- [ ] Generate those fields and types using HotChocolate type extension or
  dynamic type-module APIs.
- [ ] Add validation that selected generated flat fields are compatible with the
  requested `expand`.
- [ ] Add validation that `where`, `by`, `having`, and `order` only reference
  generated flat fields compatible with the requested `expand`.
- [ ] Add execution middleware or equivalent planning logic that expands rows
  after validation and before result materialization.
- [ ] Generate filtering, sorting, aggregate, and grouped-query support over the
  explicit flat row types.
- [ ] Add an `@export` directive for root-level `Flat` and `FlatGroup`
  selections.
- [ ] Add HTTP result-serialization support that can emit `text/csv` for
  exported root flat rowsets.

## Phase 1: Canonical Surface

- [ ] Keep flatten aligned with the product-level spec in `MISSION.md`.
- [ ] Implement the canonical surfaces as:
  - [ ] `<field>Flat(expand:, where:, order:, offset:, limit:, maxDepth:)`
  - [ ] `<field>FlatAggregate(expand:, where:, having:)`
  - [ ] `<field>FlatGroup(expand:, by:, where:, having:, order:, offset:, limit:)`
- [ ] Support `expand` supplied either as literals or variables.
- [ ] Keep GraphQL aliases as response-level renaming only.
- [ ] Derive generated field prefixes from the terminal path segment and
  lengthen them only when needed for uniqueness.
- [ ] Support `@export(format: CSV, separator:, includeHeader:, fileName:)` on
  root `<field>Flat` and root `<field>FlatGroup`.

## Phase 2: Path Resolution and Host Scoping

- [ ] Parse multi-level dot-paths such as `details.coupons`, `orders.items.tags`,
  and `items.tags`.
- [ ] Resolve path segments against the current host type context.
- [ ] Support repeated names at different levels without ambiguity.
- [ ] Allow flat sibling generation on nested collection-valued fields, not only
  root fields.
- [ ] Validate collection traversal semantics and optional depth limits.

## Phase 3: Schema Strategy

- [ ] Generate one flat row type and field family per host type rather than one
  schema shape per expand combination.
- [ ] Avoid redundant ancestor-prefixed duplicates such as both `zA` and `yzA`
  on the same host type unless a longer derived prefix is required for
  uniqueness.
- [ ] Preserve nullability, scalar, and enum semantics on generated fields.
- [ ] Ensure schema growth is bounded by host types and reachable flattenable
  leaf fields rather than expand-combination count.

## Phase 4: Cross-Apply Semantics

- [ ] Support a single `expand` source.
- [ ] Support multiple `expand` sources in one flat field call.
- [ ] For multiple `expand` sources, emit the cross-apply-style cartesian
  combination of the source rowsets per outer row.
- [ ] Define the empty-source behavior explicitly for outer rows with no
  reachable combinations.

## Phase 5: Generated Column Naming

- [ ] Generate deterministic flat-column names from
  `derivedPrefix + PascalCase(fieldName)`.
- [ ] Start default derivation from the terminal path segment.
- [ ] Lengthen the derived prefix to the minimal unique path suffix when
  collisions occur.
- [ ] Preserve compatibility with GraphQL aliases on the result field and
  selected generated fields.

## Phase 6: Execution

- [ ] Expand the configured paths into flat rowsets.
- [ ] Preserve selected outer values on every emitted row.
- [ ] Support optional `where` filtering over the flat rowset using the same
  generated field names exposed in the selection set.
- [ ] Support optional `order`, `offset`, and `limit` over the flat rowset using
  the explicit flat row type.
- [ ] Implement `FlatAggregate` and `FlatGroup` over the expanded flat rowset
  using the same aggregate planner model as the main aggregation family.
- [ ] Serialize exported root flat rowsets using selected scalar and enum leaf
  fields in document order.
- [ ] Serialize exported root grouped-flat rowsets by traversing `key` and
  aggregate operator selections to scalar and enum leaves.
- [ ] Use explicit leaf aliases as CSV headers when present; otherwise derive
  headers from the leaf response path joined with `_`.
- [ ] Reject duplicate export header names after alias and response-path
  resolution.
- [ ] Reject invalid `separator` values that do not coerce to a single
  character.
- [ ] Reject `@export` on nested flat fields, on non-flat fields, or on root
  `FlatAggregate`.
- [ ] Reject operations that combine `@export` with multiple root fields.
- [ ] Stream CSV rows where practical rather than buffering the full file.
- [ ] Reject generated-field selections that do not belong to the requested
  `expand`.
- [ ] Reject `where`, `by`, `having`, and grouped `order` clauses that
  reference generated fields that do not belong to the requested `expand`.
- [ ] Perform literal-`expand` validation during validation/planning when
  possible, and perform variable-`expand` validation after argument coercion but
  before row expansion.
- [ ] Prefer provider-backed row expansion where feasible.
- [ ] Avoid repeated traversal of the same path.

## Phase 7: Tests

- [ ] Single-source flat field tests.
- [ ] Multi-level flatten tests.
- [ ] Nested local flat field tests.
- [ ] Multi-source cross-apply flat field tests.
- [ ] Default-prefix-derivation tests.
- [ ] Prefix-collision and minimal-unique-suffix tests.
- [ ] Path-resolution tests for repeated names across levels.
- [ ] Flat-with-filter tests.
- [ ] Flat ordering and slicing tests.
- [ ] Flat aggregate tests.
- [ ] Flat grouped-aggregation tests.
- [ ] Root flat CSV export tests.
- [ ] Root flat-group CSV export tests.
- [ ] Export-header alias and response-path derivation tests.
- [ ] Invalid export target and nested-export validation tests.
- [ ] Invalid export separator and duplicate-header validation tests.
- [ ] Generated-field-selection validation tests.
- [ ] `where` / `by` / `having` / `order` field compatibility validation tests.
- [ ] Variable-`expand` request validation tests.
- [ ] Integration tests for the `underlyings` + `coupons` + `calls` security
  example.
