# Project Tasks: HotChocolate Collection Enhancements

This document outlines the hierarchical breakdown of work for extending the HotChocolate GraphQL server.

## Phase 1: Project Foundation & Setup

- [ ] 1.1. Initial project folder structure (`src`, `tests`, `doc`).
- [ ] 1.2. Add core dependencies to the `.csproj` file (`HotChocolate.AspNetCore`).
- [ ] 1.3. Create the test project structure within the `tests` directory.
- [ ] 1.4. Add testing dependencies to the test project (`Microsoft.NET.Test.Sdk`, `xunit`, `Moq`).
- [ ] 1.5. Define core data models for testing (`Security`, `Coupon`, etc.) based on `GEMINI.md`.
- [ ] 1.6. Set up a basic in-memory GraphQL server for integration testing.
- [ ] 1.7. Create an extensive set of GrapQL query test cases ordered by increasing level of sophisticated/complexity.
    - [ ] 1.7.1 Create a few test queries for default generated behaviour for backward compatibility
    - [ ] 1.7.2 expand examples `#N01` from `GEMINI.md`: Create extensive test queries for (nested) collection operations (filter/sort/slice)
    - [ ] 1.7.3 expand examples `#N02` from `GEMINI.md`: Create extensive test queries for grouping and aggregation
    - [ ] 1.7.4 expand examples `#N03` from `GEMINI.md`: Create extensive test queries for root level filtering based on inner Collection filtered size
    - [ ] 1.7.5 expand examples `#N04` from `GEMINI.md`: Create extensive test queries for flattening (at different levels, one level down, two level down)
    - [ ] 1.7.6 expand examples `#N05` from `GEMINI.md`: Create extensive test queries for all these features combined

## Phase 2: (Nested) Collection Operations (Filter, Sort, Slice)

- [ ] 2.1. **Schema Extension**:
    - [ ] 2.1.1. Design and implement the input types for `where`, `order`, `skip`, `take`, `first`, and `last` arguments.
    - [ ] 2.1.2. Implement a `TypeInterceptor` to dynamically add these arguments to all collection fields in the GraphQL schema.
- [ ] 2.2. **Resolver Logic**:
    - [ ] 2.2.1. Create a middleware or visitor to intercept the resolver pipeline for fields that have the new arguments.
    - [ ] 2.2.2. Implement the logic to translate the GraphQL arguments into `IQueryable` expression trees (`Where`, `Order`, `Skip`, `Take`).
- [ ] 2.3. **Testing**:
    - [ ] 2.3.1. Write unit tests for the expression tree generation logic.
    - [ ] 2.3.2. Write integration tests using the test server to verify end-to-end functionality, using queries setup in phase 1.7.2 above.

## Phase 3: Aggregation Capabilities

- [ ] 3.1. **Schema Extension (New Design)**:
    - [ ] 3.1.1. Implement a `TypeInterceptor` to dynamically add `xxxAggregation` fields for each collection (e.g., `underlyings` -> `underlyingsAggregation`).
    - [ ] 3.1.2. Design and implement the `xxxAggregationResult` output type. It must contain top-level fields for grouping keys and a nested `aggregates` object.
    - [ ] 3.1.3. The `aggregates` object within the result type will contain fields for each SQL-like function (`count`, `sum`, `avg`, etc.).
    - [ ] 3.1.4. Design and implement the input types for `group`, `having`, `order` (on aggregates), `skip`, and `take`.
- [ ] 3.2. **Aggregation Logic**:
    - [ ] 3.2.1. Implement the resolver logic for the `xxxAggregation` fields.
    - [ ] 3.2.2. The resolver must translate the GraphQL arguments into a single, chained `IQueryable` expression tree: `Where` -> `GroupBy` -> `OrderBy` -> `Select` -> `Skip` -> `Take`.
    - [ ] 3.2.3. The `Select` projection must map the grouped and aggregated data into the nested `xxxAggregationResult` object structure.
- [ ] 3.3. **Testing**:
    - [ ] 3.3.1. Update the test queries in file from task 1.7.3 to reflect the new aggregation schema.
    - [ ] 3.3.2. Write integration tests to cover all aggregation functions, grouping, `having` clauses, and post-aggregation sorting/slicing.

## Phase 4: Root Filtering by Nested Collection Size

- [ ] 4.1. **Schema Extension**:
    - [ ] 4.1.1. Extend the root-level filtering input types (e.g., `SecurityFilterInput`) to include fields for nested collections.
    - [ ] 4.1.2. The collection filter field should accept a `count` argument with comparison operators (`gt`, `lt`, `eq`, etc.).
- [ ] 4.2. **Query Logic**:
    - [ ] 4.2.1. Implement a custom `FilteringHandler` or extend HotChocolate's filtering provider.
    - [ ] 4.2.2. This handler must translate the `count` argument into an `IQueryable` `Where` clause on the root query (e.g., `...Where(s => s.Details.Coupons.Count() > 5)`).
    - [ ] 4.2.3. Ensure the logic correctly handles `where` clauses applied *within* the collection filter (e.g., `...Where(s => s.Details.Coupons.Count(c => c.PaymentDate > date) > number)`).
- [ ] 4.3. **Testing**:
    - [ ] 4.3.1. Write integration tests with queries setup in phase 1.7.4 above.

## Phase 5: `@flatten` Directive

- [ ] 5.1. **Directive Definition**:
    - [ ] 5.1.1. Define the `@flatten` directive. It must be applicable to fields and accept a `paths: [FlattenPathInput!]!` argument.
    - [ ] 5.1.2. Define the `FlattenPathInput` input object with two fields: `field: String!` and `prefix: String!`.
    - [ ] 5.1.3. Register the directive and its input types with the schema.
- [ ] 5.2. **Flattening Logic (Result Middleware)**:
    - [ ] 5.2.1. Implement the directive using a HotChocolate `ResultMiddleware`. This ensures it runs *after* the field's main resolver.
    - [ ] 5.2.2. The middleware logic must orchestrate the execution of the child resolvers specified in the `paths` argument.
    - [ ] 5.2.3. It must then perform a cross-product join on the results of the child resolvers.
    - [ ] 5.2.4. Finally, it must combine the parent object context with each row of the cross-product, apply the specified prefixes, and construct the new, flattened result set.
- [ ] 5.3. **Testing**:
    - [ ] 5.3.1. Update the test queries in file from task 1.7.5 to reflect the new `@flatten` directive usage.
    - [ ] 5.3.2. Write integration tests to verify the flattened output for both standard collections and `xxxAggregation` fields.
    - [ ] 5.3.3. Write integration tests for the combined feature query from task 1.7.6.

## Phase 6: Finalization & Documentation

- [ ] 6.1. Run the comprehensive integration tests defined in task 1.7.6 to verify the correct interaction of all implemented features.
- [ ] 6.2. Code review and refactoring for clarity, performance, and maintainability.
- [ ] 6.3. Refactor Code for Alternate Feature Driven Code Structure, and review code vs previous implementation for clarity/maintainability.
- [ ] 6.4. Choose one of the code organisation structures (tech driven or feature driven).
- [ ] 6.5. Add comprehensive XML comments to all public APIs.
- [ ] 6.6. Create or Update `README.md` with project goals, installation, and usage examples.
- [ ] 6.7. Write detailed usage documentation in the `doc` directory, sections for each feature and examples in increasing level of sophistication.

