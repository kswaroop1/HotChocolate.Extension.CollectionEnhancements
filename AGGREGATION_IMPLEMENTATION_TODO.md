# Aggregation - Implementation TODO

## Phase 1: Canonical Surface

- [ ] Keep the public entry point as `.AddCollectionEnhancements()`.
- [ ] Keep `MISSION.md`, this file, and `AGGREGATION_USAGE_EXAMPLES.md` aligned.
- [ ] Keep `AGGREGATION_IMPLEMENTATION_NOTE.md` aligned with the canonical aggregate surface.
- [ ] Ensure all documented examples map to the shared example domain.
- [ ] Keep the canonical direct-list query shape and avoid an `items` wrapper unless separately documented.
- [ ] Define the v1 eligible-collection-field rule explicitly and keep tests aligned with it.

## Phase 2: Raw Collection Querying

- [ ] Extend eligible collection fields with `where`, `order`, `offset`, and `limit`.
- [ ] Reuse HotChocolate filter and sort conventions for `where` and `order`.
- [ ] Add offset/limit argument types and middleware integration.
- [ ] Ensure nested collection fields receive the same treatment as root-level collection fields when appropriate.

## Phase 3: Aggregate Field Generation

- [ ] Generate `<field>Aggregate(where:, having:)` sibling fields.
- [ ] Keep `<field>Aggregate` and `<field>Group` as separate public schema fields even if they share one internal planner.
- [ ] Lock the canonical aggregate SDL shape for `<field>Aggregate` and `<field>Group`.
- [ ] Return `null` from `<field>Aggregate` when post-aggregation `having` eliminates the logical row.
- [ ] Generate aggregate result types exposing:
  - [ ] `count`
  - [ ] `countDistinct`
  - [ ] `sum`
  - [ ] `avg`
  - [ ] `min`
  - [ ] `max`
  - [ ] `stdev`
  - [ ] `stdevp`
  - [ ] `stringAgg`
  - [ ] `stringAggDistinct`
- [ ] Add numeric-only aggregate operators:
  - [ ] `skew`
  - [ ] `kurtosis`
- [ ] Enforce numeric-field applicability for `stdev`, `stdevp`, `skew`, and `kurtosis`.
- [ ] Do not add a `select` input. Use the GraphQL selection set as the select list.
- [ ] Define field eligibility and result-type shapes for `countDistinct`.
- [ ] Define argument and result-type shapes for `stringAgg` and `stringAggDistinct`.
- [ ] Support multi-member operator projection wherever the operator result type exposes more than one eligible field.

## Phase 4: Group Field Generation

- [ ] Generate `<field>Group(by:, where:, having:, order:, offset:, limit:)` sibling fields.
- [ ] Generate grouped row types containing:
  - [ ] `key`
  - [ ] aggregate operator fields directly on the row
- [ ] Define grouped ordering input that can target key fields, aggregate operator fields, or both.
- [ ] Keep grouped parent-filter semantics existential.
- [ ] Return an empty list from `<field>Group` when no grouped rows remain after filtering.

## Phase 5: Aggregate Filter Inputs

- [ ] Define canonical `having` filter inputs for aggregate operators.
- [ ] Support `and`, `or`, and `not` composition.
- [ ] Support conditional `count(where: ...)`.
- [ ] Support numeric comparisons for `stdev`, `stdevp`, `skew`, and `kurtosis`.

## Phase 6: Parent Filter Integration

- [ ] Extend nested filter inputs with `<field>Aggregate` criteria.
- [ ] Extend nested filter inputs with `<field>Group` criteria.
- [ ] Define group-filter semantics as existential: a parent matches if at least one group satisfies the predicate.
- [ ] Translate nested aggregate filter criteria into provider-backed expressions where possible.
- [ ] Build parent aggregate/group predicates through custom HotChocolate filter/provider hooks rather than assuming built-in nested filtering already understands them.

## Phase 7: Provider Execution and Performance

- [ ] Normalize `Aggregate` internally to the no-key grouped case if it simplifies planning, without exposing grouped row semantics publicly.
- [ ] Plan aggregate execution from the selection set so only requested operators are computed.
- [ ] Produce one aggregate or grouped projection per field where the provider supports it.
- [ ] Prevent one-pass-per-operator behavior in in-memory fallback.
- [ ] Implement one-pass running-moment logic for `stdev`, `stdevp`, `skew`, and `kurtosis` when provider pushdown is unavailable.
- [ ] Benchmark grouped aggregation, higher moments, `countDistinct`, and `stringAgg` scenarios.

## Phase 8: Tests

- [ ] Direct-list query-shape tests without an `items` wrapper.
- [ ] Raw collection query tests.
- [ ] Aggregate field tests.
- [ ] Group field tests.
- [ ] `having` tests.
- [ ] Nested parent filter tests.
- [ ] Coupon-count parent filter tests.
- [ ] Date-bounded aggregate-total parent filter tests.
- [ ] Distinct and string aggregation tests.
- [ ] Multi-member operator projection tests.
- [ ] Standard deviation tests.
- [ ] Skew and kurtosis tests.
- [ ] Invalid numeric-operator usage tests.
- [ ] Alias behavior tests.
