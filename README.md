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

## Test Coverage

Run the suite with coverage using the repo-level settings file:

```powershell
dotnet test tests\HotChocolate.Extension.CollectionEnhancements.Tests\HotChocolate.Extension.CollectionEnhancements.Tests.csproj --settings coverage.runsettings --collect:"XPlat Code Coverage"
```

## Packaging

Recommended publish shape:

- `HotChocolate.Extension.CollectionEnhancements.<version>.nupkg`
  - `lib/net10.0/HotChocolate.Extension.CollectionEnhancements.dll`
  - `analyzers/dotnet/cs/HotChocolate.Extension.CollectionEnhancements.Generators.dll`
  - `README.md`
  - `LICENSE`
- `HotChocolate.Extension.CollectionEnhancements.<version>.snupkg`
  - symbols/source package for debugging the runtime and generator

The runtime package is the only package consumers should need. It carries the
runtime extension assembly plus the Roslyn generator as an analyzer payload, so
`PackageReference` gives both build-time generated metadata/types and runtime
`.AddCollectionEnhancements()` support.

For local source consumption:

```xml
<ItemGroup>
  <ProjectReference Include="..\src\HotChocolate.Extension.CollectionEnhancements\HotChocolate.Extension.CollectionEnhancements.csproj" />
  <ProjectReference Include="..\src\HotChocolate.Extension.CollectionEnhancements.Generators\HotChocolate.Extension.CollectionEnhancements.Generators.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false"
                    PrivateAssets="all" />
</ItemGroup>
```

## Runtime Options

`AddCollectionEnhancements()` now accepts an optional configuration delegate:

```csharp
builder.Services
    .AddGraphQLServer()
    .AddCollectionEnhancements(options =>
    {
        options.StatisticalMomentsExecutionMode = StatisticalMomentsExecutionMode.Stable;
    });
```

Statistical moment execution defaults to `Fast`:

- SQL Server and PostgreSQL use provider-native variance and stddev functions when possible
- Oracle, SQLite, and other relational EF Core providers use relational SQL-compatible raw-moment aggregation
- stable mode and unsupported providers project only the required numeric scalar through `IQueryable` before applying C# iteration
- full row materialization is reserved for the final fallback when query projection/composition is unavailable

Set `StatisticalMomentsExecutionMode` to `Stable` to force the slower Welford-style in-memory path for `var`, `varp`, `stdev`, `stdevp`, `skew`, and `kurtosis`.
