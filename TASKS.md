# Project Tasks: HotChocolate Collection Enhancements

`TASKS.md` is the top-level project tracker.

Detailed implementation breakdowns live in:

- `AGGREGATION_IMPLEMENTATION_TODO.md`
- `FLATTEN_IMPLEMENTATION_TODO.md`

The canonical feature specs live in:

- `MISSION.md`
- `AGGREGATION_PRD.md`
- `FLATTEN_PRD.md`

## Phase 1: Foundation

- [x] Create the solution, source project, and test project.
- [x] Add core HotChocolate and test dependencies.
- [x] Create test models and an in-memory GraphQL test server.
- [ ] Finish the canonical public API rename to `.AddCollectionEnhancements()` everywhere.
- [ ] Build canonical test fixtures from the shared example domain in `MISSION.md`.
- [ ] Align baseline query tests with the canonical direct-collection query shape rather than an `items` wrapper.
- [ ] Keep the example-to-test canon up to date in `EXAMPLE_TEST_MATRIX.md`.
- [ ] Document and implement the feature mapping onto HotChocolate schema-generation, validation, provider, and middleware hooks.

## Phase 2: Aggregation Family

Detailed checklist: `AGGREGATION_IMPLEMENTATION_TODO.md`

- [ ] Implement raw collection querying with `where`, `order`, `offset`, and `limit`.
- [ ] Keep `Aggregate` and `Group` as separate public fields while allowing one shared internal planner.
- [ ] Implement `<field>Aggregate(where:, having:)`.
- [ ] Implement `<field>Group(by:, where:, having:, order:, offset:, limit:)`.
- [ ] Make `<field>Aggregate(having:)` nullable when the post-aggregation row is eliminated.
- [ ] Make `<field>Group(...)` return an empty list when no grouped rows remain after filtering.
- [ ] Add `stdev` and `stdevp` for numeric fields.
- [ ] Add `skew` and `kurtosis` if the implementation remains single-pass.
- [ ] Implement nested parent filtering by aggregate and grouped criteria.

## Phase 3: Flatten Family

Detailed checklist: `FLATTEN_IMPLEMENTATION_TODO.md`

- [ ] Implement `<field>Flat(expand:, where:, order:, offset:, limit:, maxDepth:)`.
- [ ] Implement `<field>FlatAggregate(expand:, where:, having:)`.
- [ ] Implement `<field>FlatGroup(expand:, by:, where:, having:, order:, offset:, limit:)`.
- [ ] Keep flat row types and selections statically declared in the schema rather than relying on runtime return-type mutation.
- [ ] Support cross-apply-style flattening across multiple nested collection paths.
- [ ] Support path resolution even when segment names repeat at different levels.
- [ ] Reuse the shared rowset, aggregate, and grouped-query features over flat-row output.
- [ ] Add flat-row integration coverage, especially for the `underlyings` + `coupons` + `calls` security example.

## Phase 4: Performance and Execution Quality

- [ ] Make aggregate execution selection-set-aware.
- [ ] Avoid repeated scans for separately selected aggregate operators.
- [ ] Keep `stdev`, `stdevp`, `skew`, and `kurtosis` one-pass in fallback execution.
- [ ] Prefer provider-backed grouped, aggregate, and flat-row projections.
- [ ] Add benchmarks for grouped aggregation, higher moments, distinct aggregation, and flat-row expansion.

## Phase 5: Documentation and Finalization

- [ ] Keep `MISSION.md`, `AGGREGATION_PRD.md`, and `FLATTEN_PRD.md` in sync.
- [ ] Keep `AGGREGATION_IMPLEMENTATION_NOTE.md` in sync with the aggregation PRD.
- [ ] Ensure every example query in the docs becomes an integration test.
- [ ] Ensure every documented example runs exactly as written, without conceptual translation into a different query shape.
- [ ] Expand usage examples only with scenarios covered by the shared example domain.
- [ ] Add negative integration tests for invalid numeric aggregate usage and invalid flat expand/path/field combinations.
- [ ] Add XML comments to public APIs.
