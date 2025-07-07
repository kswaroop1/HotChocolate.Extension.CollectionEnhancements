# HotChocolate 15 Architecture Deep Dive

## Table of Contents
1. [Core Architecture Patterns](#core-architecture-patterns)
2. [Feature Implementation Patterns](#feature-implementation-patterns) 
3. [Type System Architecture](#type-system-architecture)
4. [TypeInterceptor System](#typeinterceptor-system)
5. [Directive Architecture](#directive-architecture)
6. [Convention System](#convention-system)
7. [Middleware System](#middleware-system)
8. [Extension Points](#extension-points)
9. [Code Locations Reference](#code-locations-reference)

---

## Core Architecture Patterns

### Universal Feature Pattern

Every HotChocolate feature follows the exact same 4-layer architecture:

```
[Attribute] → [Convention] → [Generic Types] → [Runtime Middleware]
     ↓             ↓              ↓                ↓
UseFiltering → FilterConvention → FilterInputType<T> → IQueryBuilder
UseSorting   → SortConvention   → SortInputType<T>   → IQueryBuilder  
UsePaging    → PagingConvention → ConnectionType<T>  → IQueryBuilder
UseProjection → ProjectionConv. → (middleware only) → IQueryBuilder
```

This pattern is **universal** - you can extend it for any new feature.

### Attribute Processing Flow

**Discovery Phase** (Schema Building):
```
1. AddQueryType<MyQuery>()
   ↓
2. SchemaBuilder.AddRootType(typeof(MyQuery))
   ↓  
3. ObjectFieldDescriptor reflects over MyQuery methods
   ↓
4. DefaultTypeInspector.ApplyAttributes() 
   ↓
5. attributeProvider.GetCustomAttributes(true)  // .NET reflection
   ↓
6. foreach attribute.TryConfigure(context, descriptor, member)
   ↓
7. UseFilteringAttribute.OnConfigure() calls descriptor.UseFiltering()
```

**Key Insight**: No registration needed - pure .NET reflection + inheritance polymorphism!

### Recursive Type Generation

**How `FilterInputType<Person>` auto-generates all property filters:**

```
1. FilterConvention.GetFieldType(typeof(Person))
   ↓
2. TryCreateFilterType() → creates FilterInputType<Person>  
   ↓
3. FilterInputTypeDescriptor<Person>.OnCompleteFields()
   ↓
4. FieldDescriptorUtilities.AddImplicitFields() reflects over Person properties
   ↓
5. For each property: FilterFieldDescriptor.New()
   ↓
6. FilterFieldDescriptor constructor calls convention.GetFieldType(property.Type)
   ↓
7. RECURSIVE CALL: Back to step 1 for property type
```

**Cycle Detection**: Uses GraphQL's TypeReference system - no infinite loops!

---

## Feature Implementation Patterns

### 1. Filtering (HotChocolate.Data)

**File Locations**:
- `HotChocolate\Data\src\Data\Filters\Attributes\UseFilteringAttribute.cs`
- `HotChocolate\Data\src\Data\Filters\Extensions\FilterObjectFieldDescriptorExtensions.cs` 
- `HotChocolate\Data\src\Data\Filters\Convention\FilterConvention.cs`
- `HotChocolate\Data\src\Data\Filters\FilterInputType`1.cs`

**Implementation Pattern**:
```csharp
// 1. Attribute (delegates to extension)
[UseFiltering]
protected override void OnConfigure(...) 
{
    descriptor.UseFiltering(Scope);
}

// 2. Extension Method (applies convention + middleware)
public static IObjectFieldDescriptor UseFiltering(this IObjectFieldDescriptor descriptor)
{
    var convention = context.GetFilterConvention();
    var filterType = convention.GetFieldType(entityType);
    // Add argument and middleware
}

// 3. Convention (generates types via reflection)
public virtual ExtendedTypeReference GetFieldType(MemberInfo member)
{
    if (TryCreateFilterType(_typeInspector.GetReturnType(member, true), out var rt))
        return _typeInspector.GetTypeRef(rt, TypeContext.Input, Scope);
}

// 4. Type Generation (recursive reflection)
protected bool TryCreateFilterType(IExtendedType runtimeType, out Type? type)
{
    if (runtimeType.Type.IsClass || runtimeType.Type.IsInterface)
    {
        type = typeof(FilterInputType<>).MakeGenericType(runtimeType.Source);
        return true;
    }
}

// 5. Middleware (runtime execution)
WellKnownMiddleware.Filtering → IQueryBuilder.Apply() → LINQ translation
```

### 2. Operation Filter Types

**Type Hierarchy**:
```
FilterInputType (base)
├── FilterInputType<T> (generic entity filter)
├── ListFilterInputType<T> (collection operations: all, some, none, any)
└── Operation Filters:
    ├── StringOperationFilterInputType (eq, neq, contains, startsWith, etc.)
    ├── ComparableOperationFilterInputType<T> (gt, gte, lt, lte, etc.)
    │   ├── IntOperationFilterInputType
    │   ├── DecimalOperationFilterInputType  
    │   ├── DateTimeOperationFilterInputType
    │   └── ...
    ├── EnumOperationFilterInputType<T>
    └── BooleanOperationFilterInputType
```

**Pattern**: Each operation type defines available GraphQL operations:
```csharp
public class StringOperationFilterInputType : FilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        descriptor.Operation(DefaultFilterOperations.Equals).Type<StringType>();
        descriptor.Operation(DefaultFilterOperations.Contains).Type<StringType>().Expensive();
        descriptor.Operation(DefaultFilterOperations.In).Type<ListType<StringType>>();
        // etc...
    }
}
```

### 3. Sorting (HotChocolate.Data)

**File Locations**:
- `HotChocolate\Data\src\Data\Sorting\Attributes\UseSortingAttribute.cs`
- `HotChocolate\Data\src\Data\Sorting\SortInputType`1.cs`
- `HotChocolate\Data\src\Data\Sorting\Types\DefaultSortEnumType.cs`

**Pattern**: Same 4-layer pattern as filtering:
```csharp
[UseSorting] → SortConvention → SortInputType<T> → WellKnownMiddleware.Sorting
```

**Sort Enum**:
```csharp
public class DefaultSortEnumType : SortEnumType
{
    protected override void Configure(ISortEnumTypeDescriptor descriptor)
    {
        descriptor.Operation(DefaultSortOperations.Ascending);   // ASC
        descriptor.Operation(DefaultSortOperations.Descending);  // DESC
    }
}
```

### 4. Paging (HotChocolate.Core)

**Two Implementations**:
- **Cursor Paging**: `ConnectionType<T>`, `EdgeType<T>` (Relay spec)
- **Offset Paging**: `CollectionSegmentType<T>`, `OffsetPageInfoType`

**File Locations**:
- `HotChocolate\Core\src\Types.CursorPagination\Extensions\UsePagingAttribute.cs`
- `HotChocolate\Core\src\Types.CursorPagination\ConnectionType.cs`
- `HotChocolate\Core\src\Types.OffsetPagination\Extensions\UseOffsetPagingAttribute.cs`

### 5. Projections (HotChocolate.Data)  

**Unique Pattern**: No new GraphQL types - pure middleware optimization
```csharp
[UseProjection] → ProjectionConvention → (no new types) → Selection optimization
```

Uses existing GraphQL selection sets to generate efficient database projections.

### 6. Slicing Operations (HotChocolate.Data)

**Existing**:
- `UseFirstOrDefaultAttribute` → `WellKnownMiddleware.SingleOrDefault`
- `UseSingleOrDefaultAttribute`

**Pattern**: Simple middleware without new argument types (hardcoded behavior).

---

## TypeInterceptor System

### Complete Lifecycle Hooks

HotChocolate provides a comprehensive `TypeInterceptor` system that allows you to hook into every stage of schema building:

**File Location**: `HotChocolate\Core\src\Types\Configuration\TypeInterceptor.cs`

### Schema Building Lifecycle

```csharp
public abstract class TypeInterceptor
{
    // 1. SCHEMA PREPARATION
    internal virtual void OnBeforeCreateSchemaInternal(IDescriptorContext context, ISchemaBuilder schemaBuilder) { }
    internal virtual void InitializeContext(IDescriptorContext context, TypeInitializer typeInitializer, 
        TypeRegistry typeRegistry, TypeLookup typeLookup, TypeReferenceResolver typeReferenceResolver) { }
        
    // 2. TYPE DISCOVERY PHASE
    public virtual void OnBeforeDiscoverTypes() { }
    public virtual void OnAfterDiscoverTypes() { }
    internal virtual bool SkipDirectiveDefinition(DirectiveDefinitionNode node) => false;
    
    // 3. TYPE INITIALIZATION PHASE  
    public virtual void OnBeforeInitialize(ITypeDiscoveryContext discoveryContext) { }
    public virtual void OnAfterInitialize(ITypeDiscoveryContext discoveryContext, DefinitionBase definition) { }
    public virtual IEnumerable<TypeReference> RegisterMoreTypes(IReadOnlyCollection<ITypeDiscoveryContext> discoveryContexts) => [];
    public virtual void OnTypeRegistered(ITypeDiscoveryContext discoveryContext) { }
    public virtual void OnTypesInitialized() { }
    
    // 4. DEPENDENCY REGISTRATION PHASE
    public virtual void OnBeforeRegisterDependencies(ITypeDiscoveryContext discoveryContext, DefinitionBase definition) { }
    public virtual void OnAfterRegisterDependencies(ITypeDiscoveryContext discoveryContext, DefinitionBase definition) { }
    
    // 5. NAME COMPLETION PHASE
    public virtual void OnBeforeCompleteTypeNames() { }
    public virtual void OnBeforeCompleteName(ITypeCompletionContext completionContext, DefinitionBase definition) { }
    public virtual void OnAfterCompleteName(ITypeCompletionContext completionContext, DefinitionBase definition) { }
    public virtual void OnAfterCompleteTypeNames() { }
    public virtual void OnTypesCompletedName() { }
    
    // 6. TYPE EXTENSION MERGING
    public virtual void OnBeforeMergeTypeExtensions() { }
    public virtual void OnAfterMergeTypeExtensions() { }
    
    // 7. TYPE COMPLETION PHASE
    public virtual void OnBeforeCompleteTypes() { }
    public virtual void OnBeforeCompleteType(ITypeCompletionContext completionContext, DefinitionBase definition) { }
    internal virtual void OnBeforeCompleteMutation(ITypeCompletionContext completionContext, ObjectTypeDefinition definition) { }
    public virtual void OnBeforeCompleteMutationField(ITypeCompletionContext completionContext, ObjectFieldDefinition mutationField) { }
    public virtual void OnAfterCompleteType(ITypeCompletionContext completionContext, DefinitionBase definition) { }
    public virtual void OnAfterCompleteTypes() { }
    public virtual void OnTypesCompleted() { }
    
    // 8. ROOT TYPE RESOLUTION
    public virtual void OnAfterResolveRootType(ITypeCompletionContext completionContext, 
        ObjectTypeDefinition definition, OperationType operationType) { }
    
    // 9. METADATA COMPLETION
    public virtual void OnBeforeCompleteMetadata() { }
    public virtual void OnBeforeCompleteMetadata(ITypeCompletionContext context, DefinitionBase definition) { }
    public virtual void OnAfterCompleteMetadata(ITypeCompletionContext context, DefinitionBase definition) { }
    public virtual void OnAfterCompleteMetadata() { }
    
    // 10. EXECUTABLE PREPARATION
    public virtual void OnBeforeMakeExecutable() { }
    public virtual void OnBeforeMakeExecutable(ITypeCompletionContext context, DefinitionBase definition) { }
    public virtual void OnAfterMakeExecutable(ITypeCompletionContext context, DefinitionBase definition) { }
    public virtual void OnAfterMakeExecutable() { }
    
    // 11. VALIDATION
    public virtual void OnValidateType(ITypeSystemObjectContext validationContext, DefinitionBase definition) { }
    
    // 12. SCHEMA FINALIZATION
    internal virtual void OnBeforeRegisterSchemaTypes(IDescriptorContext context, SchemaTypesDefinition schemaTypesDefinition) { }
    internal virtual void OnAfterCreateSchemaInternal(IDescriptorContext context, ISchema schema) { }
    public virtual void OnCreateSchemaError(IDescriptorContext context, Exception error) { }
    
    // UTILITY METHODS
    public virtual bool IsEnabled(IDescriptorContext context) => true;
    internal virtual uint Position => uint.MaxValue / 2;  // For ordering interceptors
    public virtual bool TryCreateScope(ITypeDiscoveryContext discoveryContext, 
        out IReadOnlyList<TypeDependency>? typeDependencies) { ... }
}
```

### Real Implementation Examples

**Interface Completion Interceptor** (`InterfaceCompletionTypeInterceptor.cs:18-151`):
```csharp
internal sealed class InterfaceCompletionTypeInterceptor : TypeInterceptor
{
    private readonly Dictionary<ITypeSystemObject, TypeInfo> _typeInfos = new();
    private readonly Dictionary<Type, TypeInfo> _allInterfaceRuntimeTypes = new();
    
    public override void OnAfterInitialize(ITypeDiscoveryContext discoveryContext, DefinitionBase definition)
    {
        // Preserve initialization context of interface and object types
        if (definition is IComplexOutputTypeDefinition typeDefinition)
        {
            _typeInfos.Add(discoveryContext.Type, new(discoveryContext, typeDefinition));
        }
    }
    
    public override void OnTypesInitialized()
    {
        // Index runtime types of all interfaces
        foreach (var interfaceTypeInfo in _typeInfos.Values
            .Where(t => t.Definition.RuntimeType is { } rt && 
                rt != typeof(object) && 
                t.Definition is InterfaceTypeDefinition))
        {
            _allInterfaceRuntimeTypes.Add(interfaceTypeInfo.Definition.RuntimeType, interfaceTypeInfo);
        }
        
        // Infer interface usage from runtime types
        foreach (var typeInfo in _typeInfos.Values.Where(IsRelevant))
        {
            TryInferInterfaceFromRuntimeType(GetRuntimeType(typeInfo), 
                _allInterfaceRuntimeTypes.Keys, _interfaceRuntimeTypes);
                
            // Register interface dependencies and update type definition
            foreach (var interfaceRuntimeType in _interfaceRuntimeTypes)
            {
                var interfaceTypeInfo = _allInterfaceRuntimeTypes[interfaceRuntimeType];
                var dependency = new TypeDependency(interfaceTypeInfo.Context.TypeReference, 
                    TypeDependencyFulfilled.Completed);
                
                typeInfo.Context.Dependencies.Add(dependency);
                typeInfo.Definition.Interfaces.Add(dependency.Type);
            }
        }
    }
}
```

### Custom TypeInterceptor Implementation

```csharp
public class CustomFeatureTypeInterceptor : TypeInterceptor
{
    public override void OnAfterInitialize(ITypeDiscoveryContext discoveryContext, DefinitionBase definition)
    {
        // Modify type definitions during initialization
        if (definition is ObjectTypeDefinition objectDef)
        {
            // Add custom fields, modify existing ones, etc.
        }
    }
    
    public override IEnumerable<TypeReference> RegisterMoreTypes(
        IReadOnlyCollection<ITypeDiscoveryContext> discoveryContexts)
    {
        // Dynamically add more types based on discovered types
        var additionalTypes = new List<TypeReference>();
        
        foreach (var context in discoveryContexts)
        {
            if (ShouldGenerateCustomType(context))
            {
                additionalTypes.Add(TypeReference.Create(typeof(MyGeneratedType<>)
                    .MakeGenericType(context.Type.RuntimeType)));
            }
        }
        
        return additionalTypes;
    }
    
    public override void OnBeforeCompleteType(ITypeCompletionContext completionContext, DefinitionBase definition)
    {
        // Final modifications before type completion
        if (definition is ObjectTypeDefinition objectDef)
        {
            // Add middleware, modify field configurations, etc.
        }
    }
}
```

**Registration**:
```csharp
services.AddGraphQLServer()
    .TryAddTypeInterceptor<CustomFeatureTypeInterceptor>();
```

---

## Directive Architecture

### 4-Component Directive System

HotChocolate directives follow a consistent 4-component architecture:

```
[Directive Model] → [DirectiveType<T>] → [DirectiveAttribute<T>] → [DirectiveMiddleware]
     ↓                    ↓                     ↓                      ↓
AuthorizationInput → AuthorizeDirectiveType → AuthorizeAttribute → Authorization middleware
FlattenInput      → FlattenDirectiveType   → FlattenAttribute   → Flattening middleware
```

### Component Breakdown

**1. Directive Model** (POCO):
```csharp
public sealed class AuthorizeDirective
{
    public string? Policy { get; set; }
    public string[]? Roles { get; set; }
    public AuthorizeApplyPolicy Apply { get; set; } = AuthorizeApplyPolicy.BeforeResolver;
}
```

**2. DirectiveType<T>** (Schema Definition):
```csharp
public sealed class AuthorizeDirectiveType : DirectiveType<AuthorizeDirective>
{
    protected override void Configure(IDirectiveTypeDescriptor<AuthorizeDirective> descriptor)
    {
        descriptor.Name("authorize")
            .Location(DirectiveLocation.Object)
            .Location(DirectiveLocation.FieldDefinition)
            .Repeatable();
            
        descriptor.Argument(t => t.Policy)
            .Type<StringType>();
            
        descriptor.Argument(t => t.Roles)
            .Type<ListType<NonNullType<StringType>>>();
            
        descriptor.Argument(t => t.Apply)
            .Type<ApplyPolicyType>()
            .DefaultValue(AuthorizeApplyPolicy.BeforeResolver);
    }
}
```

**3. DirectiveAttribute<T>** (Easy Application):
```csharp
public sealed class AuthorizeAttribute : DirectiveAttribute<AuthorizeDirective>
{
    public AuthorizeAttribute(string? policy = null) 
        : base(new AuthorizeDirective { Policy = policy }) { }
        
    public AuthorizeAttribute(params string[] roles) 
        : base(new AuthorizeDirective { Roles = roles }) { }
}
```

**4. DirectiveMiddleware** (Runtime Processing):
```csharp
public delegate ValueTask DirectiveMiddleware(
    IDirectiveContext context,
    DirectiveDelegate next);

// Usage in schema configuration
builder.AddDirectiveType<AuthorizeDirectiveType>()
    .UseDirective<AuthorizeDirective>(AuthorizeMiddleware.Invoke);
```

### Base Classes and Infrastructure

**DirectiveType<T>** (`HotChocolate\Core\src\Types\Types\DirectiveType~1.cs`):
```csharp
public abstract class DirectiveType<TDirective> : DirectiveType where TDirective : class
{
    protected sealed override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        var typedDescriptor = DirectiveTypeDescriptor.Create<TDirective>(Context);
        Configure(typedDescriptor);
        descriptor.Extend(typedDescriptor);
    }
    
    protected abstract void Configure(IDirectiveTypeDescriptor<TDirective> descriptor);
}
```

**DirectiveAttribute<T>** (`HotChocolate\Core\src\Types\Types\Attributes\DirectiveAttribute.cs:13-96`):
```csharp
public abstract class DirectiveAttribute<TDirective> : DescriptorAttribute where TDirective : class
{
    private readonly TDirective _directive;
    
    protected DirectiveAttribute(TDirective directive)
    {
        _directive = directive ?? throw new ArgumentNullException(nameof(directive));
    }
    
    protected internal sealed override void TryConfigure(
        IDescriptorContext context,
        IDescriptor descriptor,
        ICustomAttributeProvider element)
    {
        // Supports all descriptor types
        switch (descriptor)
        {
            case ArgumentDescriptor desc: desc.Directive(_directive); break;
            case DirectiveArgumentDescriptor desc: desc.Directive(_directive); break;
            case EnumTypeDescriptor desc: desc.Directive(_directive); break;
            case EnumValueDescriptor desc: desc.Directive(_directive); break;
            case InputFieldDescriptor desc: desc.Directive(_directive); break;
            case InputObjectTypeDescriptor desc: desc.Directive(_directive); break;
            case InterfaceFieldDescriptor desc: desc.Directive(_directive); break;
            case InterfaceTypeDescriptor desc: desc.Directive(_directive); break;
            case ObjectFieldDescriptor desc: desc.Directive(_directive); break;
            case ObjectTypeDescriptor desc: desc.Directive(_directive); break;
            case SchemaTypeDescriptor desc: desc.Directive(_directive); break;
            case UnionTypeDescriptor desc: desc.Directive(_directive); break;
            default: throw new ArgumentOutOfRangeException(nameof(descriptor));
        }
        
        OnConfigure(context, _directive, element);
    }
    
    protected virtual void OnConfigure(IDescriptorContext context, TDirective descriptor, 
        ICustomAttributeProvider element) { }
}
```

### Custom Directive Implementation Example

**@flatten Directive for Nested Collections**:

```csharp
// 1. Directive Model
public sealed class FlattenDirective
{
    public string Path { get; set; } = default!;
    public string? Alias { get; set; }
    public int? MaxDepth { get; set; }
}

// 2. DirectiveType
public sealed class FlattenDirectiveType : DirectiveType<FlattenDirective>
{
    protected override void Configure(IDirectiveTypeDescriptor<FlattenDirective> descriptor)
    {
        descriptor.Name("flatten")
            .Location(DirectiveLocation.FieldDefinition)
            .Description("Flattens nested collection properties into the parent selection.");
            
        descriptor.Argument(t => t.Path)
            .Type<NonNullType<StringType>>()
            .Description("The property path to flatten (e.g., 'orders.items').");
            
        descriptor.Argument(t => t.Alias)
            .Type<StringType>()
            .Description("Optional alias for the flattened field.");
            
        descriptor.Argument(t => t.MaxDepth)
            .Type<IntType>()
            .Description("Maximum nesting depth to flatten.");
    }
}

// 3. DirectiveAttribute
public sealed class FlattenAttribute : DirectiveAttribute<FlattenDirective>
{
    public FlattenAttribute(string path, string? alias = null, int? maxDepth = null) 
        : base(new FlattenDirective { Path = path, Alias = alias, MaxDepth = maxDepth }) { }
}

// 4. Middleware Implementation
public static class FlattenMiddleware
{
    public static async ValueTask InvokeAsync(
        IDirectiveContext context,
        DirectiveDelegate next)
    {
        var directive = context.Directive.ToObject<FlattenDirective>();
        
        // Store flattening configuration for field resolver
        context.SetLocalState("flatten.config", directive);
        
        await next(context);
        
        // Post-process result to apply flattening
        if (context.Result is IQueryable queryable)
        {
            var flattened = ApplyFlattening(queryable, directive);
            context.Result = flattened;
        }
    }
    
    private static IQueryable ApplyFlattening(IQueryable source, FlattenDirective directive)
    {
        // Implementation: Use Expression trees to flatten nested collections
        // This would build SelectMany expressions based on the Path configuration
        // Supporting multi-level flattening with MaxDepth limits
        
        return source; // Simplified for brevity
    }
}

// 5. Registration
services.AddGraphQLServer()
    .AddDirectiveType<FlattenDirectiveType>()
    .UseDirective<FlattenDirective>(FlattenMiddleware.InvokeAsync);
```

**Usage Examples**:
```csharp
public class Query
{
    [Flatten("orders.items")]
    public IQueryable<Customer> GetCustomers([Service] IDbContext context) => context.Customers;
    
    [Flatten("posts.comments", maxDepth: 2)]
    public IQueryable<User> GetUsers([Service] IDbContext context) => context.Users;
}
```

```graphql
query {
  flatItems: customers @flatten(path: "orders.items") {
    name
    productName
    quantity
  }
}
```

---

## Type System Architecture

### Base Class Hierarchy

```
DescriptorAttribute (abstract base)
├── ObjectFieldDescriptorAttribute (sealed TryConfigure)
│   ├── UseFilteringAttribute
│   ├── UseSortingAttribute  
│   ├── UsePagingAttribute
│   ├── UseProjectionAttribute
│   ├── UseFirstOrDefaultAttribute
│   └── UseSingleOrDefaultAttribute
├── InputFieldDescriptorAttribute
├── ArgumentDescriptorAttribute
└── ... (other descriptor types)
```

**Key Method**:
```csharp
// Base class - called by reflection
protected internal abstract void TryConfigure(
    IDescriptorContext context,
    IDescriptor descriptor,
    ICustomAttributeProvider element);

// Intermediate class - type-safe dispatch  
protected internal sealed override void TryConfigure(...)
{
    if (descriptor is IObjectFieldDescriptor d && element is MemberInfo m)
        OnConfigure(context, d, m);
}

// Concrete class - actual implementation
protected override void OnConfigure(
    IDescriptorContext context,
    IObjectFieldDescriptor descriptor, 
    MemberInfo member) 
{
    descriptor.UseFiltering(); // Delegate to extension method
}
```

### Convention System

**Base Convention Class** (`HotChocolate\Core\src\Types\Types\Descriptors\Conventions\Convention.cs`):
```csharp
public abstract class Convention : IConvention
{
    private string? _scope;
    
    public string? Scope
    {
        get => _scope;
        protected set
        {
            if (IsInitialized)
                throw new InvalidOperationException("The convention scope is immutable.");
            _scope = value;
        }
    }
    
    protected bool IsInitialized { get; private set; }
    
    protected internal virtual void Initialize(IConventionContext context)
    {
        MarkInitialized();
    }
    
    protected internal virtual void Complete(IConventionContext context) { }
    
    protected void MarkInitialized()
    {
        if (IsInitialized)
            throw new InvalidOperationException($"The convention {GetType().Name} has already been marked as initialized.");
        IsInitialized = true;
    }
}
```

**Generic Convention Pattern** (`Convention<TDefinition>`):
```csharp
public abstract class Convention<TDefinition> : Convention where TDefinition : class
{
    private TDefinition? _definition;
    
    protected internal sealed override void Initialize(IConventionContext context)
    {
        _definition = CreateDefinition(context);
        base.Initialize(context);
    }
    
    protected abstract TDefinition CreateDefinition(IConventionContext context);
    
    public TDefinition Definition => _definition ?? 
        throw new InvalidOperationException("Convention not initialized.");
}
```

**Real Convention Implementation**:
```csharp
public class FilterConvention : Convention<FilterConventionDefinition>, IFilterConvention
{
    protected override FilterConventionDefinition CreateDefinition(IConventionContext context)
    {
        var descriptor = FilterConventionDescriptor.New(context.DescriptorContext, Scope);
        Configure(descriptor);
        return descriptor.CreateDefinition();
    }
    
    protected virtual void Configure(IFilterConventionDescriptor descriptor)
    {
        // Configure default operation types, bindings, providers
        descriptor.BindRuntimeType<string, StringOperationFilterInputType>();
        descriptor.BindRuntimeType<int, IntOperationFilterInputType>();
        descriptor.Provider<QueryableFilterProvider>();
    }
    
    protected internal override void Complete(IConventionContext context)
    {
        // Initialize runtime state after all types are discovered
        _typeInspector = context.DescriptorContext.TypeInspector;
        _provider = Definition.Provider;
    }
    
    // Convention-specific methods
    public virtual ExtendedTypeReference GetFieldType(MemberInfo member)
    {
        if (TryCreateFilterType(_typeInspector.GetReturnType(member, true), out var runtimeType))
        {
            return _typeInspector.GetTypeRef(runtimeType, TypeContext.Input, Scope);
        }
        throw new InvalidOperationException($"Unable to create filter type for {member}.");
    }
    
    protected virtual bool TryCreateFilterType(IExtendedType runtimeType, out Type? type)
    {
        if (runtimeType.Type.IsClass || runtimeType.Type.IsInterface)
        {
            type = typeof(FilterInputType<>).MakeGenericType(runtimeType.Source);
            return true;
        }
        
        type = null;
        return false;
    }
}
```

**Convention Registration and Discovery**:
```csharp
// Built-in registration methods
services.AddGraphQLServer()
    .AddFiltering()    // → Registers FilterConvention
    .AddSorting()      // → Registers SortConvention  
    .AddProjections(); // → Registers ProjectionConvention

// Custom convention registration  
builder.AddConvention<IFilterConvention, CustomFilterConvention>();
builder.AddConvention<IMyCustomConvention>(new MyCustomConvention());

// Convention with scope
builder.AddConvention<IFilterConvention, CustomFilterConvention>("customScope");
```

**Convention Extension Pattern**:
```csharp
public class CustomFilterConvention : FilterConvention
{
    protected override void Configure(IFilterConventionDescriptor descriptor)
    {
        base.Configure(descriptor); // Keep defaults
        
        // Add custom operations
        descriptor.Operation(CustomOperations.SoundexMatch)
            .Name("soundex")
            .Description("Matches using soundex algorithm");
            
        // Add custom type bindings
        descriptor.BindRuntimeType<CustomEntity, CustomEntityFilterInputType>();
        
        // Override provider for custom query translation
        descriptor.Provider<CustomQueryableFilterProvider>();
    }
    
    protected override bool TryCreateFilterType(IExtendedType runtimeType, out Type? type)
    {
        // Custom type generation logic
        if (runtimeType.Source == typeof(GeoPoint))
        {
            type = typeof(GeoPointFilterInputType);
            return true;
        }
        
        return base.TryCreateFilterType(runtimeType, out type);
    }
}
```

---

## Middleware System  

### Well-Known Middleware Constants

From `HotChocolate\Core\src\Abstractions\WellKnownMiddleware.cs`:

**Data Query Middleware**:
```csharp
public static class WellKnownMiddleware
{
    public const string Filtering = "HotChocolate.Data.Filtering";
    public const string Sorting = "HotChocolate.Data.Sorting"; 
    public const string Paging = "HotChocolate.Types.Paging";
    public const string Projection = "HotChocolate.Data.Projection";
    public const string SingleOrDefault = "HotChocolate.Data.SingleOrDefault";
    public const string DataLoader = "HotChocolate.Fetching.DataLoader";
    public const string DbContext = "HotChocolate.Data.EF.UseDbContext";
    public const string ToList = "HotChocolate.Data.EF.ToList";
    // ... 20+ more middleware types
}
```

### IQueryBuilder Pattern

**All data features implement `IQueryBuilder`**:
```csharp
public interface IQueryBuilder
{
    void Prepare(IMiddlewareContext context);  // Parse GraphQL arguments
    void Apply(IMiddlewareContext context);    // Transform IQueryable<T>
}
```

**Middleware Integration**:
```csharp
// From UnwrapFieldMiddlewareHelper.cs
internal static FieldMiddleware CreateDataMiddleware(IQueryBuilder builder)
    => next =>
    {
        return async ctx =>
        {
            builder.Prepare(ctx);              // 1. Parse arguments
            await next(ctx).ConfigureAwait(false);  // 2. Execute resolver
            
            if (ctx.Result is not IFieldResult fieldResult)
                builder.Apply(ctx);            // 3. Apply query transformation
        };
    };
```

**Pipeline Order**:
```
Resolver → [Projection] → [Filtering] → [Sorting] → [Paging] → [SingleOrDefault]
```

---

## Convention System

### Convention Discovery and Registration

**Built-in Conventions** (automatically registered):
```csharp
services.AddGraphQLServer()
    .AddFiltering()    // → FilterConvention
    .AddSorting()      // → SortConvention  
    .AddProjections()  // → ProjectionConvention
```

**Custom Convention Registration**:
```csharp
builder.AddConvention<IFilterConvention, CustomFilterConvention>();
```

### Convention Extension Points

**Filter Convention Extensions**:
```csharp
public class CustomFilterConvention : FilterConvention
{
    protected override void Configure(IFilterConventionDescriptor descriptor)
    {
        base.Configure(descriptor);
        
        // Add custom operations
        descriptor.Operation(MyCustomOperations.SoundexMatch);
        
        // Add custom bindings
        descriptor.BindRuntimeType<CustomType, CustomFilterInputType>();
        
        // Add custom provider
        descriptor.Provider<CustomFilterProvider>();
    }
    
    protected bool TryCreateFilterType(IExtendedType runtimeType, out Type? type)
    {
        // Custom type generation logic
        if (runtimeType.Source == typeof(MySpecialType))
        {
            type = typeof(MySpecialFilterInputType);
            return true; 
        }
        
        return base.TryCreateFilterType(runtimeType, out type);
    }
}
```

---

## Extension Points

### 1. Creating New Attributes

**Pattern**: Inherit from `ObjectFieldDescriptorAttribute`:
```csharp
public sealed class UseMyFeatureAttribute : ObjectFieldDescriptorAttribute
{
    protected override void OnConfigure(
        IDescriptorContext context,
        IObjectFieldDescriptor descriptor, 
        MemberInfo member)
    {
        descriptor.UseMyFeature(); // Delegate to extension method
    }
}
```

### 2. Creating Extension Methods

**Pattern**: Extend `IObjectFieldDescriptor`:
```csharp
public static class MyFeatureObjectFieldDescriptorExtensions
{
    public static IObjectFieldDescriptor UseMyFeature(
        this IObjectFieldDescriptor descriptor)
    {
        return descriptor.Use(next => async context =>
        {
            // Custom middleware logic
            await next(context);
        });
    }
}
```

### 3. Creating Conventions

**Pattern**: Inherit from `Convention<TDefinition>`:
```csharp
public class MyFeatureConvention : Convention<MyFeatureConventionDefinition>
{
    protected override MyFeatureConventionDefinition CreateDefinition(IConventionContext context)
    {
        var descriptor = MyFeatureConventionDescriptor.New(context.DescriptorContext);
        Configure(descriptor);
        return descriptor.CreateDefinition();
    }
    
    protected virtual void Configure(IMyFeatureConventionDescriptor descriptor) { }
}
```

### 4. Creating Input Types

**Pattern**: Inherit from appropriate base:
```csharp
// For entity-based types
public class MyInputType<T> : InputObjectType
{
    protected override void Configure(IInputObjectTypeDescriptor descriptor)
    {
        descriptor.Field("myField").Type<StringType>();
    }
}

// For operation-based types  
public class MyOperationFilterInputType : FilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        descriptor.Operation(MyOperations.CustomOp).Type<StringType>();
    }
}
```

### 5. Creating Query Builders

**Pattern**: Implement `IQueryBuilder`:
```csharp
public class MyFeatureQueryBuilder<T> : IQueryBuilder
{
    public void Prepare(IMiddlewareContext context)
    {
        var args = context.ArgumentValue<MyArgs>("myArgs");
        context.SetLocalState("myArgs", args);
    }
    
    public void Apply(IMiddlewareContext context) 
    {
        var source = context.Result as IQueryable<T>;
        var args = context.GetLocalState<MyArgs>("myArgs");
        
        // Transform the queryable
        var result = source.Where(/* build expression */);
        context.Result = result;
    }
}
```

---

## Code Locations Reference

### Core Architecture Files
```
HotChocolate\Core\src\Abstractions\WellKnownMiddleware.cs
HotChocolate\Core\src\Types\Types\Attributes\DescriptorAttribute.cs  
HotChocolate\Core\src\Types\Types\Attributes\ObjectFieldDescriptorAttribute.cs
HotChocolate\Core\src\Types\Types\Descriptors\Conventions\DefaultTypeInspector.cs
HotChocolate\Core\src\Types\Extensions\SchemaBuilderExtensions.Types.cs
```

### Filtering Implementation
```
HotChocolate\Data\src\Data\Filters\Attributes\UseFilteringAttribute.cs
HotChocolate\Data\src\Data\Filters\Extensions\FilterObjectFieldDescriptorExtensions.cs
HotChocolate\Data\src\Data\Filters\Convention\FilterConvention.cs
HotChocolate\Data\src\Data\Filters\FilterInputType`1.cs
HotChocolate\Data\src\Data\Filters\FilterInputTypeDescriptor`1.cs
HotChocolate\Data\src\Data\Filters\FilterFieldDescriptor.cs
HotChocolate\Data\src\Data\Extensions\UnwrapFieldMiddlewareHelper.cs
```

### Operation Filter Types
```
HotChocolate\Data\src\Data\Filters\Types\StringOperationFilterInputType.cs
HotChocolate\Data\src\Data\Filters\Types\IntOperationFilterInputType.cs
HotChocolate\Data\src\Data\Filters\Types\ComparableOperationFilterInputType.cs
HotChocolate\Data\src\Data\Filters\Types\EnumOperationFilterInputType.cs
HotChocolate\Data\src\Data\Filters\Types\ListFilterInputType.cs
```

### Sorting Implementation  
```
HotChocolate\Data\src\Data\Sorting\Attributes\UseSortingAttribute.cs
HotChocolate\Data\src\Data\Sorting\SortInputType`1.cs
HotChocolate\Data\src\Data\Sorting\SortInputTypeDescriptor`1.cs
HotChocolate\Data\src\Data\Sorting\Types\DefaultSortEnumType.cs
```

### Paging Implementation
```
HotChocolate\Core\src\Types.CursorPagination\Extensions\UsePagingAttribute.cs
HotChocolate\Core\src\Types.CursorPagination\ConnectionType.cs
HotChocolate\Core\src\Types.CursorPagination\EdgeType.cs
HotChocolate\Core\src\Types.OffsetPagination\Extensions\UseOffsetPagingAttribute.cs
```

### Slicing Implementation
```
HotChocolate\Data\src\Data\Projections\Attributes\UseFirstOrDefaultAttribute.cs
HotChocolate\Data\src\Data\Projections\Attributes\UseSingleOrDefaultAttribute.cs
```

### Common Infrastructure
```
HotChocolate\Data\src\Data\IQueryBuilder.cs
HotChocolate\Data\src\Data\Extensions\UnwrapFieldMiddlewareHelper.cs
```

---

## Extension Points

### Complete Extension Checklist

Based on all discovered patterns, here are the comprehensive extension points for building custom features:

**1. TypeInterceptor Extensions**:
```csharp
// Hook into any schema building phase
public class MyTypeInterceptor : TypeInterceptor
{
    public override void OnAfterInitialize(ITypeDiscoveryContext discoveryContext, DefinitionBase definition) { }
    public override IEnumerable<TypeReference> RegisterMoreTypes(IReadOnlyCollection<ITypeDiscoveryContext> discoveryContexts) => [];
    public override void OnBeforeCompleteType(ITypeCompletionContext completionContext, DefinitionBase definition) { }
}

// Register
services.AddGraphQLServer().TryAddTypeInterceptor<MyTypeInterceptor>();
```

**2. Directive Extensions** (4-component pattern):
```csharp
// Model + DirectiveType + Attribute + Middleware
public class MyDirective { public string Value { get; set; } }
public class MyDirectiveType : DirectiveType<MyDirective> { }
public class MyAttribute : DirectiveAttribute<MyDirective> { }
public static class MyMiddleware { public static ValueTask InvokeAsync(...) { } }
```

**3. Convention Extensions**:
```csharp
// Create or extend conventions  
public interface IMyConvention : IConvention { }
public class MyConvention : Convention<MyConventionDefinition>, IMyConvention { }
builder.AddConvention<IMyConvention, MyConvention>();
```

**4. Attribute Extensions** (auto-discovery):
```csharp
// Pure .NET reflection - no registration needed
public class UseMyFeatureAttribute : ObjectFieldDescriptorAttribute
{
    protected override void OnConfigure(...) => descriptor.UseMyFeature();
}
```

**5. Type Extensions**:
```csharp
// InputObjectType, FilterInputType, etc.
public class MyInputType<T> : InputObjectType { }
public class MyOperationFilterInputType : FilterInputType { }
```

**6. Middleware Extensions**:
```csharp
// IQueryBuilder pattern for data transformations
public class MyQueryBuilder : IQueryBuilder
{
    public void Prepare(IMiddlewareContext context) { }
    public void Apply(IMiddlewareContext context) { }
}

// Simple middleware
public class MyMiddleware
{
    public async ValueTask InvokeAsync(IMiddlewareContext context, FieldDelegate next) { }
}
```

### Development Workflow for NuGet-Only Projects

**1. Reference Required Packages**:
```xml
<PackageReference Include="HotChocolate.AspNetCore" Version="15.x.x" />
<PackageReference Include="HotChocolate.Data" Version="15.x.x" />          <!-- If using data features -->
<PackageReference Include="HotChocolate.Authorization" Version="15.x.x" />  <!-- If using authorization -->
```

**2. Follow Universal Patterns**:
- All features use the 4-layer pattern: Attribute → Convention → Types → Middleware
- TypeInterceptors hook into schema building lifecycle
- Conventions handle type generation and configuration
- Attributes provide automatic discovery via .NET reflection
- Middleware handles runtime processing

**3. Use Discovered Infrastructure**:
- `WellKnownMiddleware` constants for standard ordering
- `IQueryBuilder` interface for data transformations
- `DirectiveAttribute<T>` base for directive attributes
- `Convention<TDefinition>` base for custom conventions
- `TypeInterceptor` base for schema building hooks

---

## Summary

This comprehensive architecture guide captures the complete patterns discovered in HotChocolate 15:

**Universal Patterns**:
- **4-Layer Feature Pattern**: Attribute → Convention → Types → Middleware (used by ALL features)
- **TypeInterceptor System**: 40+ lifecycle hooks for schema building  
- **Convention System**: Type generation and configuration rules
- **Directive Architecture**: 4-component pattern for schema directives
- **Attribute Discovery**: Pure .NET reflection with polymorphic dispatch
- **Middleware Pipeline**: Ordered execution with IQueryBuilder for data features

**Key Insights for External Projects**:
1. **No Registration Required**: Attributes work via .NET reflection
2. **Universal Extension Points**: Every pattern follows the same architecture
3. **Rich Lifecycle Access**: TypeInterceptors provide complete schema building control
4. **Composable Design**: Features combine naturally (filtering + sorting + paging)
5. **NuGet-Only Development**: All patterns work with package references only

This guide enables building advanced GraphQL features in separate projects using only HotChocolate NuGet packages, following the same architectural patterns used internally.
