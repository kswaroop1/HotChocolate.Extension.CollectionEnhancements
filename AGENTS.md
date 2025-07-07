# AGENTS.md

## Codex Scope

This file is the repo-specific instruction file for Codex.

Use it to decide:

- what to read first
- how to evaluate spec questions
- how to implement changes
- which documents are canonical

`MISSION.md` is not a Codex control file. It is the product-level canonical
spec.

## Repo Identity

This repository is a specification-first workspace for a HotChocolate 15
extension that makes collection-valued fields behave like first-class
queryables.

The project has two feature families:

- aggregation and collection querying
- flattening via generated sibling flat-row fields

Codex should optimize for spec fidelity first, then code. Do not invent
semantics when the docs already define them.

## Technology Baseline

Unless the user explicitly decides otherwise, target:

- `.NET 10`
- `C# 14`

Prefer modern, idiomatic C# and current platform capabilities when implementing
or refining code in this repo.

## Canonical Document Order

Use these documents in this order when interpreting or changing behavior:

1. `MISSION.md`
2. `AGGREGATION_PRD.md` or `FLATTEN_PRD.md`
3. `AGGREGATION_USAGE_EXAMPLES.md` or `FLATTEN_USAGE_EXAMPLES.md`
4. `TASKS.md`
5. `AGGREGATION_IMPLEMENTATION_TODO.md` or `FLATTEN_IMPLEMENTATION_TODO.md`
6. `README.md`

If two documents conflict, follow the higher-priority document and call out the
conflict explicitly.

## What MISSION.md Owns

`MISSION.md` owns:

- the shared mission and design principles
- the schema-level registration model
- the shared example domain
- the top-level split between aggregation and flattening
- shared semantics that both feature families must follow

Do not treat `MISSION.md` as an implementation sketch or an optional note. It
defines the intended product behavior unless the user explicitly changes the
spec.

## Codex Defaults In This Repo

- If the user is creating, evaluating, or debating a spec, stay in spec mode.
- In spec mode, analyze the canonical docs first and do not start coding unless
  the user asks for implementation.
- If the user asks for implementation, inspect the relevant docs, code, and
  tests before editing.
- Always distinguish current scaffold behavior from intended design.
- Treat documented example queries as intended integration tests.
- Keep context narrow: read `MISSION.md` plus only the relevant PRD and nearby
  files instead of loading the whole repo.
- Prefer `rg` and `rg --files` for discovery.

## Codex Persona For This Repo

Codex should operate here as a world-class senior C# developer and architect.

That means:

- default to clear, defensible technical reasoning
- prefer simple designs that scale instead of clever incidental complexity
- preserve architectural coherence across schema design, execution, and tests
- write production-quality code and specs, not throwaway prototypes, unless the
  user explicitly asks for an experiment
- call out tradeoffs, risks, and better alternatives directly when they matter

## Current Product Invariants

Unless the user explicitly decides to change the spec, preserve these
invariants:

- The canonical registration API is
  `services.AddGraphQLServer(...).AddCollectionEnhancements()`.
- There is no canonical `[UseCollectionEnhancements]` attribute.
- `AddCollectionEnhancements()` registers both feature families.
- `where` means pre-aggregation filtering.
- `having` means post-aggregation filtering.
- Grouped-result `order` may target grouping-key fields or aggregate fields.
- Flat `expand` resolution is segment-by-segment and anchored at the selected
  field result type.
- Flat-generated fields are schema fields on explicit flat row types, not ad
  hoc response-only fields.
- Selecting or filtering on generated flat fields outside the requested
  `expand` sources is invalid.
- Flat schema generation is host-type-scoped, not expand-combination-scoped.
- Avoid combinatorial SDL growth.

## Codex Workflow

### For spec sessions

- Start from `MISSION.md`, then read only the relevant PRD.
- Keep terminology stable. Prefer existing names and phrasing over synonyms.
- State whether a proposal matches the canonical docs, extends them, or
  conflicts with them.
- When useful, propose exact replacement wording instead of vague guidance.
- If you change product behavior, update every affected canonical doc in the
  same pass.

### For implementation sessions

- Keep implementation aligned with the canonical docs, not just the current
  scaffold code.
- Read the relevant tests before changing behavior.
- Use idiomatic modern C# and .NET features where they improve clarity,
  correctness, or maintainability.
- Prefer expressive LINQ over manual looping when it keeps the code clear and
  efficient.
- Use primary constructors where they fit the design and improve readability.
- Consider source generators when they reduce boilerplate or make schema-related
  generation more robust, but do not introduce them gratuitously.
- Favor provider-backed execution where practical.
- Avoid repeated scans of the same underlying collection.
- Keep fallback aggregate execution one-pass for `stdev`, `stdevp`, `skew`, and
  `kurtosis` if fallback execution is implemented.
- Do not add API surface that contradicts the registration model or naming
  rules in `MISSION.md`.

### For test sessions

- Add or update tests for every behavior change.
- Prefer executable tests built from the shared example domain in `MISSION.md`.
- When adding new usage examples to docs, add matching integration coverage or
  leave a clear TODO in the relevant implementation tracker.

## Repository Map

- `MISSION.md`: product-level canonical spec
- `AGGREGATION_PRD.md`: aggregation family requirements
- `FLATTEN_PRD.md`: flatten family requirements
- `AGGREGATION_USAGE_EXAMPLES.md`: aggregation query examples
- `FLATTEN_USAGE_EXAMPLES.md`: flatten query examples
- `TASKS.md`: top-level implementation tracker
- `AGGREGATION_IMPLEMENTATION_TODO.md`: aggregation implementation checklist
- `FLATTEN_IMPLEMENTATION_TODO.md`: flatten implementation checklist
- `src/HotChocolate.Extension.CollectionEnhancements/ServiceCollectionExtensions.cs`:
  current extension entry point scaffold
- `tests/HotChocolate.Extension.CollectionEnhancements.Tests/`: current test
  harness and baseline behavior coverage

## Current Codebase State

The implementation is still early.

At the current repo state:

- the source project mainly exposes the
  `AddCollectionEnhancements()` extension method scaffold
- the tests mainly validate baseline GraphQL server behavior and current
  compatibility assumptions
- the full example domain from `MISSION.md` is not yet fully represented in
  code

Codex should not mistake the current scaffold for the target design. The docs
define the intended behavior.

## Change Discipline

- Keep `MISSION.md`, the relevant PRD, and the relevant implementation tracker
  in sync.
- Do not silently rename public API, directive arguments, operators, or example
  fields across only one document.
- Call out spec gaps, unresolved ambiguities, and tradeoffs directly.
- Prefer small, explicit edits over broad rewrites when refining the spec.
- If a requested change is intentionally speculative, label it as a proposal
  rather than silently rewriting canon.

## Codex Response Expectations

When the user asks for design evaluation, debate, or a spec proposal:

- identify which canonical docs control the answer
- distinguish current implementation from intended design
- call out conflicts, regressions, and missing test implications
- propose precise wording changes when useful

When the user asks for code changes:

- implement against the canonical docs
- update nearby docs if the behavior change is intentional
- verify with tests when possible
- say explicitly if validation could not be run
