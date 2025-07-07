# HotChocolate 15 Advanced Implementation Patterns

## Table of Contents
1. [Advanced Type System](#advanced-type-system)
2. [Advanced Middleware Patterns](#advanced-middleware-patterns)  
3. [Data Provider Integration](#data-provider-integration)
4. [Performance & Optimization](#performance--optimization)
5. [Code Examples & Templates](#code-examples--templates)

---

## Advanced Type System

### Interface Type Implementation

**Base Interface Pattern**:
```csharp
// Generic interface type (most common)
public class InterfaceType<T> : InterfaceType
{
    protected override InterfaceTypeDefinition CreateDefinition(ITypeDiscoveryContext context)
    {
        var descriptor = InterfaceTypeDescriptor.New<T>(context.DescriptorContext);
        _configure!(descriptor);
        _configure = null;
        
        // Apply configuration from attributes
        context.DescriptorContext.TypeConfiguration.Apply(typeof(T), descriptor);
        
        return descriptor.CreateDefinition();
    }
    
    protected virtual void Configure(IInterfaceTypeDescriptor<T> descriptor) { }
}
```

**Interface Inheritance Pattern**:
```csharp
// Interface implementing other interfaces
public class EntityInterface : InterfaceType<IEntity>
{
    protected override void Configure(IInterfaceTypeDescriptor<IEntity> descriptor)
    {
        descriptor.Name("Entity")
            .Description("Base entity interface");
            
        descriptor.Field("id")
            .Type<NonNullType<IdType>>();
            
        descriptor.Field("createdAt")
            .Type<NonNullType<DateTimeType>>();
    }
}

public class NamedEntityInterface : InterfaceType<INamedEntity>
{
    protected override void Configure(IInterfaceTypeDescriptor<INamedEntity> descriptor)
    {
        descriptor.Name("NamedEntity")
            .Description("Entity with name");
            
        // Implement Entity interface
        descriptor.Implements<EntityInterface>();
        
        descriptor.Field("name")
            .Type<NonNullType<StringType>>();
    }
}

// Object implementing multiple interfaces
public class PersonType : ObjectType<Person>
{
    protected override void Configure(IObjectTypeDescriptor<Person> descriptor)
    {
        descriptor.Implements<EntityInterface>()
                  .Implements<NamedEntityInterface>();
                  
        descriptor.Field("age")
            .Type<IntType>();
    }
}
```

### Union Type Implementation

**Basic Union Pattern**:
```csharp
public class SearchResultUnion : UnionType
{
    protected override void Configure(IUnionTypeDescriptor descriptor)
    {
        descriptor.Name("SearchResult")
            .Description("Union of possible search results");
            
        descriptor.Type<PersonType>();
        descriptor.Type<CompanyType>();
        descriptor.Type<ProductType>();
    }
}

// Usage in resolver
public class Query
{
    public IEnumerable<object> Search(string term)
    {
        // Return heterogeneous results
        return new object[]
        {
            new Person { Name = "John" },
            new Company { Name = "Acme Corp" },
            new Product { Name = "Widget" }
        };
    }
}
```

**Advanced Union with Type Resolution**:
```csharp
public class SearchResultUnion : UnionType
{
    protected override void Configure(IUnionTypeDescriptor descriptor)
    {
        descriptor.Name("SearchResult");
        
        descriptor.Type<PersonType>();
        descriptor.Type<CompanyType>();
        descriptor.Type<ProductType>();
        
        // Custom type resolution
        descriptor.ResolveAbstractType((context, result) =>
        {
            return result switch
            {
                Person => context.Schema.GetType<PersonType>(),
                Company => context.Schema.GetType<CompanyType>(),
                Product => context.Schema.GetType<ProductType>(),
                _ => null
            };
        });
    }
}
```

### Custom Scalar Types

**Basic Scalar Pattern**:
```csharp
public class EmailType : ScalarType<string>
{
    public EmailType() : base("Email", BindingBehavior.Implicit) { }

    protected override string ParseLiteral(IValueNode valueSyntax)
    {
        if (valueSyntax is StringValueNode stringLiteral)
        {
            if (IsValidEmail(stringLiteral.Value))
                return stringLiteral.Value;
        }
        
        throw new SerializationException("Invalid email format.", this);
    }

    public override IValueNode ParseValue(object? runtimeValue)
    {
        if (runtimeValue is string email && IsValidEmail(email))
            return new StringValueNode(email);
            
        throw new SerializationException("Invalid email format.", this);
    }

    public override bool TrySerialize(object? runtimeValue, out object? resultValue)
    {
        if (runtimeValue is string email && IsValidEmail(email))
        {
            resultValue = email;
            return true;
        }

        resultValue = null;
        return false;
    }

    public override bool TryDeserialize(object? resultValue, out object? runtimeValue)
    {
        if (resultValue is string email && IsValidEmail(email))
        {
            runtimeValue = email;
            return true;
        }

        runtimeValue = null;
        return false;
    }

    private static bool IsValidEmail(string email) => 
        !string.IsNullOrEmpty(email) && email.Contains('@');
}
```

**Complex Scalar with Custom Serialization**:
```csharp
public class MoneyType : ScalarType<Money, MoneyValueNode>
{
    public MoneyType() : base("Money", BindingBehavior.Implicit) { }

    protected override Money ParseLiteral(MoneyValueNode valueSyntax)
    {
        return new Money(valueSyntax.Amount, valueSyntax.Currency);
    }

    protected override MoneyValueNode ParseValue(Money runtimeValue)
    {
        return new MoneyValueNode(runtimeValue.Amount, runtimeValue.Currency);
    }

    public override IValueNode ParseResult(object? resultValue)
    {
        if (resultValue is Money money)
            return new MoneyValueNode(money.Amount, money.Currency);
            
        throw new SerializationException("Invalid money format.", this);
    }
}

// Custom value node
public class MoneyValueNode : IValueNode
{
    public MoneyValueNode(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }
    
    public decimal Amount { get; }
    public string Currency { get; }
    public SyntaxKind Kind => SyntaxKind.ObjectValue;
    public Location? Location => null;
}
```

### Type Extensions

**Object Type Extensions**:
```csharp
// Extend existing type with new fields
public class PersonExtension : ObjectTypeExtension<Person>
{
    protected override void Configure(IObjectTypeDescriptor<Person> descriptor)
    {
        descriptor.Name("Person"); // Must match original type name
        
        // Add computed field
        descriptor.Field("fullName")
            .Type<NonNullType<StringType>>()
            .Resolve(ctx => $"{ctx.Parent<Person>().FirstName} {ctx.Parent<Person>().LastName}");
            
        // Add field with external data
        descriptor.Field("posts")
            .Type<ListType<PostType>>()
            .Resolve(async ctx =>
            {
                var person = ctx.Parent<Person>();
                var postService = ctx.Service<IPostService>();
                return await postService.GetPostsByAuthorAsync(person.Id);
            });
    }
}

// Register extension
services.AddGraphQLServer()
    .AddQueryType<Query>()
    .AddType<PersonType>()
    .AddTypeExtension<PersonExtension>(); // Register extension
```

**Interface Type Extensions**:
```csharp
public class NodeExtension : InterfaceTypeExtension
{
    protected override void Configure(IInterfaceTypeDescriptor descriptor)
    {
        descriptor.Name("Node"); // Extend existing Node interface
        
        // Add audit fields to all Node implementers
        descriptor.Field("createdBy")
            .Type<StringType>()
            .Description("User who created this entity");
            
        descriptor.Field("lastModified")
            .Type<DateTimeType>()
            .Description("When this entity was last modified");
    }
}
```

### Generic Type Constraints

**Generic Types with Complex Constraints**:
```csharp
// Generic type with multiple constraints
public class AuditableEntityType<T> : ObjectType<T> 
    where T : class, IAuditable, IEntity
{
    protected override void Configure(IObjectTypeDescriptor<T> descriptor)
    {
        // Automatically add audit fields for all auditable entities
        descriptor.Field("createdAt")
            .Type<NonNullType<DateTimeType>>()
            .Resolve(ctx => ctx.Parent<T>().CreatedAt);
            
        descriptor.Field("updatedAt")
            .Type<DateTimeType>()
            .Resolve(ctx => ctx.Parent<T>().UpdatedAt);
            
        descriptor.Field("createdBy")
            .Type<UserType>()
            .Resolve(async ctx =>
            {
                var entity = ctx.Parent<T>();
                var userService = ctx.Service<IUserService>();
                return await userService.GetByIdAsync(entity.CreatedById);
            });
    }
}

// Usage
public class ProductType : AuditableEntityType<Product>
{
    protected override void Configure(IObjectTypeDescriptor<Product> descriptor)
    {
        base.Configure(descriptor); // Get audit fields
        
        descriptor.Field(p => p.Name);
        descriptor.Field(p => p.Price);
        // Audit fields automatically available
    }
}
```

**Generic Input Types with Validation**:
```csharp
public class PagedInputType<T> : InputObjectType<PagedInput<T>>
    where T : class
{
    protected override void Configure(IInputObjectTypeDescriptor<PagedInput<T>> descriptor)
    {
        descriptor.Field(t => t.PageSize)
            .Type<IntType>()
            .DefaultValue(20)
            .Description("Number of items per page (1-100)")
            .Validate(value =>
            {
                if (value is int pageSize && (pageSize < 1 || pageSize > 100))
                    throw new GraphQLException("Page size must be between 1 and 100");
            });
            
        descriptor.Field(t => t.Page)
            .Type<IntType>()
            .DefaultValue(1)
            .Description("Page number (1-based)")
            .Validate(value =>
            {
                if (value is int page && page < 1)
                    throw new GraphQLException("Page must be 1 or greater");
            });
    }
}
```

---

## Advanced Middleware Patterns

### Middleware Delegate Types

**Core Delegate Patterns**:
```csharp
// Field-level middleware
public delegate ValueTask FieldDelegate(IMiddlewareContext context);
public delegate FieldDelegate FieldMiddleware(FieldDelegate next);

// Request-level middleware  
public delegate ValueTask RequestDelegate(IRequestContext context);
public delegate RequestDelegate RequestMiddleware(RequestDelegate next);

// Directive middleware
public delegate FieldDelegate DirectiveMiddleware(FieldDelegate next, Directive directive);
```

### Request Pipeline Architecture

**Request Middleware Order**:
```csharp
// Global request pipeline (outermost to innermost)
services.AddGraphQLServer()
    .UseInstrumentation()           // 1. Metrics/tracing
    .UseExceptions()               // 2. Error handling  
    .UseTimeout()                  // 3. Request timeout
    .UseDocumentCache()            // 4. Document caching
    .UseDocumentParser()           // 5. Parse GraphQL document
    .UseDocumentValidation()       // 6. Validate document
    .UseOperationResolver()        // 7. Select operation
    .UseOperationExecutor();       // 8. Execute operation (→ Field Pipeline)
```

**Field Middleware Pipeline**:
```csharp
// Field pipeline (per GraphQL field, innermost to outermost)
public class CustomFieldMiddleware
{
    private readonly FieldDelegate _next;
    
    public CustomFieldMiddleware(FieldDelegate next)
    {
        _next = next;
    }
    
    public async ValueTask InvokeAsync(IMiddlewareContext context)
    {
        // Pre-processing
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Call next middleware in pipeline
            await _next(context);
            
            // Post-processing (success)
            LogSuccess(context.Selection.Field.Name, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Post-processing (error)
            LogError(context.Selection.Field.Name, ex, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}

// Registration
descriptor.Field("myField")
    .Use<CustomFieldMiddleware>()  // Applied to specific field
    .Resolve(context => "Hello");
```

### Context Propagation Patterns

**State Flow Between Middleware**:
```csharp
public class AuthenticationMiddleware
{
    public async ValueTask InvokeAsync(IMiddlewareContext context, FieldDelegate next)
    {
        // Extract user from JWT
        var user = ExtractUserFromToken(context);
        
        // Store in scoped state (available to downstream middleware)
        context.SetScopedState("currentUser", user);
        
        // Store in local state (available only to this field chain)
        context.SetLocalState("authTime", DateTimeOffset.UtcNow);
        
        await next(context);
    }
}

public class AuthorizationMiddleware
{
    public async ValueTask InvokeAsync(IMiddlewareContext context, FieldDelegate next)
    {
        // Retrieve user from upstream middleware
        var user = context.GetScopedState<User>("currentUser");
        
        if (!await IsAuthorizedAsync(user, context.Selection.Field))
        {
            throw new GraphQLException("Access denied");
        }
        
        await next(context);
    }
}

public class AuditMiddleware
{
    public async ValueTask InvokeAsync(IMiddlewareContext context, FieldDelegate next)
    {
        var user = context.GetScopedState<User>("currentUser");
        var authTime = context.GetLocalState<DateTimeOffset>("authTime");
        
        // Log field access
        await LogFieldAccessAsync(user, context.Selection.Field, authTime);
        
        await next(context);
    }
}
```

### Async Patterns in Middleware

**Proper Async/Await Usage**:
```csharp
public class DataLoaderMiddleware
{
    public async ValueTask InvokeAsync(IMiddlewareContext context, FieldDelegate next)
    {
        // ConfigureAwait(false) for library code
        await next(context).ConfigureAwait(false);
        
        if (context.Result is Task task)
        {
            // Handle async resolvers
            var result = await task.ConfigureAwait(false);
            context.Result = result;
        }
        else if (context.Result is ValueTask valueTask)
        {
            // Handle ValueTask results
            var result = await valueTask.ConfigureAwait(false);
            context.Result = result;
        }
    }
}
```

**Concurrent Execution Pattern**:
```csharp
public class ParallelExecutionMiddleware
{
    public async ValueTask InvokeAsync(IMiddlewareContext context, FieldDelegate next)
    {
        await next(context).ConfigureAwait(false);
        
        if (context.Result is IEnumerable<Task> tasks)
        {
            // Execute multiple async operations concurrently
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            context.Result = results;
        }
    }
}
```

### Error Propagation Patterns

**Error Handling Middleware**:
```csharp
public class ErrorHandlingMiddleware
{
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    
    public ErrorHandlingMiddleware(ILogger<ErrorHandlingMiddleware> logger)
    {
        _logger = logger;
    }
    
    public async ValueTask InvokeAsync(IMiddlewareContext context, FieldDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (BusinessLogicException ex)
        {
            // Convert to GraphQL error
            context.ReportError(ErrorBuilder.New()
                .SetMessage(ex.Message)
                .SetCode(ex.ErrorCode)
                .SetPath(context.Path)
                .SetLocations(context.Selection.SyntaxNode)
                .SetExtension("businessRule", ex.RuleName)
                .Build());
                
            // Set null result (GraphQL error handling)
            context.Result = null;
        }
        catch (UnauthorizedAccessException)
        {
            // Security error
            context.ReportError("Access denied");
            context.Result = null;
        }
        catch (Exception ex)
        {
            // Log unexpected errors
            _logger.LogError(ex, "Unexpected error in field {Field}", context.Selection.Field.Name);
            
            // Don't expose internal errors in production
            if (_env.IsProduction())
            {
                context.ReportError("An internal error occurred");
            }
            else
            {
                context.ReportError(ex.Message);
            }
            
            context.Result = null;
        }
    }
}
```

---

## Data Provider Integration

### Provider Architecture Overview

**Core Provider Interfaces**:
```csharp
// Base provider interface
public interface IFilterProvider
{
    IReadOnlyCollection<IFilterFieldHandler> FieldHandlers { get; }
    
    IQueryBuilder CreateBuilder<TEntityType>(string argumentName);
    
    void ConfigureField(string argumentName, IObjectFieldDescriptor descriptor);
    
    IFilterMetadata? CreateMetaData(
        ITypeCompletionContext context,
        IFilterInputTypeDefinition typeDefinition,
        IFilterFieldDefinition fieldDefinition);
}

// Similar pattern for sorting
public interface ISortProvider
{
    IReadOnlyCollection<ISortFieldHandler> FieldHandlers { get; }
    IQueryBuilder CreateBuilder<TEntityType>(string argumentName);
    void ConfigureField(string argumentName, IObjectFieldDescriptor descriptor);
}

// Projection provider
public interface IProjectionProvider : IConvention
{
    IProjectionFieldHandler CreateFieldHandler();
    IProjectionMetadata? CreateMetaData(ITypeCompletionContext context, 
        IComplexOutputTypeDefinition typeDefinition, 
        IOutputFieldDefinition fieldDefinition);
}
```

### LINQ/Entity Framework Provider

**QueryableFilterProvider Implementation**:
```csharp
public class QueryableFilterProvider : FilterProvider<QueryableFilterContext>
{
    public QueryableFilterProvider()
    {
        // Register default field handlers
        AddFieldHandler<QueryableStringFilterHandler>();
        AddFieldHandler<QueryableComparableFilterHandler>();
        AddFieldHandler<QueryableBooleanFilterHandler>();
        AddFieldHandler<QueryableEnumFilterHandler>();
        AddFieldHandler<QueryableListFilterHandler>();
        AddFieldHandler<QueryableObjectFilterHandler>();
    }

    public override IQueryBuilder CreateBuilder<TEntityType>(string argumentName)
    {
        return new QueryableFilterQueryBuilder<TEntityType>(
            argumentName, 
            this, 
            FieldHandlers);
    }

    public override void ConfigureField(string argumentName, IObjectFieldDescriptor descriptor)
    {
        // Store provider metadata on field
        descriptor.Extend().OnBeforeCreate(definition =>
        {
            definition.ContextData[QueryableFilterProvider.ContextArgumentNameKey] = argumentName;
            definition.ContextData[QueryableFilterProvider.ContextVisitFilterArgumentKey] = 
                CreateVisitFilterArgumentDelegate();
        });
    }
}
```

**Query Builder Implementation**:
```csharp
public class QueryableFilterQueryBuilder<T> : IQueryBuilder
{
    private readonly string _argumentName;
    private readonly QueryableFilterProvider _provider;
    
    public void Prepare(IMiddlewareContext context)
    {
        // Extract filter argument from GraphQL request
        var argumentValue = context.ArgumentValue<IValueNode>(_argumentName);
        
        if (argumentValue != null && !argumentValue.IsNull())
        {
            // Parse into filter expression tree
            var filterType = context.Field.Arguments[_argumentName].Type;
            var visitorContext = _provider.CreateVisitorContext(argumentValue, filterType);
            
            // Store for Apply phase
            context.SetLocalState("filter.context", visitorContext);
        }
    }
    
    public void Apply(IMiddlewareContext context)
    {
        if (context.Result is IQueryable<T> queryable &&
            context.GetLocalState<QueryableFilterContext>("filter.context") is { } filterContext)
        {
            // Build LINQ expression from filter context
            var expression = BuildFilterExpression(filterContext);
            
            // Apply to queryable
            context.Result = queryable.Where(expression);
        }
    }
    
    private Expression<Func<T, bool>> BuildFilterExpression(QueryableFilterContext context)
    {
        var parameter = Expression.Parameter(typeof(T), "entity");
        var body = BuildExpressionRecursive(context.Filters, parameter);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}
```

### MongoDB Provider Integration

**MongoDbFilterProvider Architecture**:
```csharp
public class MongoDbFilterProvider : FilterProvider<MongoDbFilterContext>
{
    public MongoDbFilterProvider()
    {
        // MongoDB-specific field handlers
        AddFieldHandler<MongoDbStringFilterHandler>();
        AddFieldHandler<MongoDbObjectIdFilterHandler>();
        AddFieldHandler<MongoDbComparableFilterHandler>();
        AddFieldHandler<MongoDbArrayFilterHandler>();
    }

    public override IQueryBuilder CreateBuilder<TEntityType>(string argumentName)
    {
        return new MongoDbFilterQueryBuilder<TEntityType>(argumentName, this);
    }
}

public class MongoDbFilterQueryBuilder<T> : IQueryBuilder
{
    public void Apply(IMiddlewareContext context)
    {
        if (context.Result is IMongoDbExecutable<T> executable &&
            context.GetLocalState<MongoDbFilterDefinition>("mongo.filter") is { } filterDef)
        {
            // Apply MongoDB filter definition
            context.Result = executable.WithFilter(filterDef);
        }
    }
}
```

**MongoDB Field Handlers**:
```csharp
public class MongoDbStringFilterHandler : IFilterFieldHandler<MongoDbFilterContext>
{
    public bool CanHandle(ITypeCompletionContext context, IFilterFieldDefinition definition)
    {
        return definition.Member?.GetMemberType().Type == typeof(string);
    }

    public void ConfigureField(IFilterFieldDefinition definition)
    {
        definition.ContextData["mongodb.fieldHandler"] = this;
    }

    public MongoDbFilterDefinition HandleField(
        MongoDbFilterContext context,
        IFilterFieldDefinition definition,
        ObjectFieldNode field)
    {
        var operation = field.Arguments[0].Name.Value; // eq, contains, startsWith, etc.
        var value = ExtractValue(field.Arguments[0].Value);
        var fieldName = definition.Member.Name;

        return operation switch
        {
            "eq" => Builders<object>.Filter.Eq(fieldName, value),
            "contains" => Builders<object>.Filter.Regex(fieldName, new BsonRegularExpression(value.ToString())),
            "startsWith" => Builders<object>.Filter.Regex(fieldName, new BsonRegularExpression($"^{value}")),
            _ => throw new NotSupportedException($"Operation {operation} not supported")
        };
    }
}
```

### Custom Provider Creation

**Custom Provider Template**:
```csharp
// Example: Redis provider for caching layer
public class RedisFilterProvider : FilterProvider<RedisFilterContext>
{
    private readonly IConnectionMultiplexer _redis;
    
    public RedisFilterProvider(IConnectionMultiplexer redis)
    {
        _redis = redis;
        
        // Register Redis-specific handlers
        AddFieldHandler<RedisStringFilterHandler>();
        AddFieldHandler<RedisNumericFilterHandler>();
        AddFieldHandler<RedisSetFilterHandler>();
    }

    public override IQueryBuilder CreateBuilder<TEntityType>(string argumentName)
    {
        return new RedisFilterQueryBuilder<TEntityType>(_redis, argumentName, this);
    }
}

public class RedisFilterQueryBuilder<T> : IQueryBuilder
{
    private readonly IDatabase _db;
    private readonly string _argumentName;
    
    public void Prepare(IMiddlewareContext context)
    {
        var filterArg = context.ArgumentValue<IValueNode>(_argumentName);
        if (filterArg != null)
        {
            var redisQuery = TranslateToRedisQuery(filterArg);
            context.SetLocalState("redis.query", redisQuery);
        }
    }

    public void Apply(IMiddlewareContext context)
    {
        if (context.GetLocalState<RedisQuery>("redis.query") is { } query)
        {
            // Execute Redis query
            var results = ExecuteRedisQuery(query);
            context.Result = results;
        }
    }
}
```

### Expression Translation Patterns

**LINQ Expression Building**:
```csharp
public static class FilterExpressionBuilder
{
    public static Expression<Func<T, bool>> BuildWhere<T>(IFilterInputType filterType, IValueNode filterValue)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var visitor = new FilterExpressionVisitor<T>(parameter);
        var body = visitor.Visit(filterType, filterValue);
        
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}

public class FilterExpressionVisitor<T>
{
    private readonly ParameterExpression _parameter;
    
    public Expression Visit(IFilterInputType filterType, IValueNode valueNode)
    {
        if (valueNode is ObjectValueNode objectValue)
        {
            return VisitObject(filterType, objectValue);
        }
        
        throw new ArgumentException("Expected object value node");
    }
    
    private Expression VisitObject(IFilterInputType filterType, ObjectValueNode objectValue)
    {
        var expressions = new List<Expression>();
        
        foreach (var field in objectValue.Fields)
        {
            var fieldDefinition = filterType.Fields[field.Name.Value];
            var fieldExpression = VisitField(fieldDefinition, field);
            expressions.Add(fieldExpression);
        }
        
        // Combine with AND
        return expressions.Aggregate(Expression.AndAlso);
    }
    
    private Expression VisitField(IFilterField fieldDefinition, ObjectFieldNode field)
    {
        var property = Expression.Property(_parameter, fieldDefinition.Member.Name);
        var value = ExtractValue(field.Value);
        
        return fieldDefinition.Handler.CreateExpression(property, value);
    }
}
```

---

## Performance & Optimization

### Query Complexity Analysis

**Custom Complexity Analyzer**:
```csharp
public class CustomComplexityAnalyzer : IDocumentAnalyzer
{
    public void Analyze(IDocumentAnalyzerContext context)
    {
        var complexity = CalculateComplexity(context.Document);
        
        if (complexity > 1000) // Max complexity limit
        {
            context.ReportError(ErrorBuilder.New()
                .SetMessage($"Query complexity {complexity} exceeds limit of 1000")
                .SetExtension("complexity", complexity)
                .Build());
        }
    }
    
    private int CalculateComplexity(DocumentNode document)
    {
        // Custom complexity calculation logic
        // Factor in: depth, breadth, expensive operations, etc.
        return 0;
    }
}

// Registration
services.AddGraphQLServer()
    .AddDocumentAnalyzer<CustomComplexityAnalyzer>();
```

### Memory Management Patterns

**Object Pooling**:
```csharp
public class PooledFilterContext : IDisposable
{
    private static readonly ObjectPool<PooledFilterContext> Pool = 
        new DefaultObjectPool<PooledFilterContext>(new PooledFilterContextPolicy());
    
    public static PooledFilterContext Rent() => Pool.Get();
    
    public void Return()
    {
        Reset();
        Pool.Return(this);
    }
    
    public void Dispose() => Return();
}

public class OptimizedFilterMiddleware
{
    public async ValueTask InvokeAsync(IMiddlewareContext context, FieldDelegate next)
    {
        using var pooledContext = PooledFilterContext.Rent();
        
        // Use pooled context for filter processing
        ProcessFilter(pooledContext, context);
        
        await next(context);
    }
}
```

---

## Code Examples & Templates

### Complete Custom Provider Template

```csharp
// 1. Define your context
public class CustomProviderContext : IVisitorContext
{
    public CustomQuery Query { get; set; } = new();
    public Stack<CustomOperator> Operations { get; } = new();
}

// 2. Create the provider
public class CustomProvider : IFilterProvider
{
    public IReadOnlyCollection<IFilterFieldHandler> FieldHandlers { get; }
    
    public CustomProvider()
    {
        var handlers = new List<IFilterFieldHandler>
        {
            new CustomStringHandler(),
            new CustomNumericHandler(),
            new CustomObjectHandler()
        };
        
        FieldHandlers = handlers.AsReadOnly();
    }

    public IQueryBuilder CreateBuilder<TEntityType>(string argumentName)
    {
        return new CustomQueryBuilder<TEntityType>(argumentName, this);
    }

    public void ConfigureField(string argumentName, IObjectFieldDescriptor descriptor)
    {
        descriptor.Extend().OnBeforeCreate(definition =>
        {
            definition.ContextData["custom.argumentName"] = argumentName;
            definition.ContextData["custom.provider"] = this;
        });
    }

    public IFilterMetadata? CreateMetaData(
        ITypeCompletionContext context,
        IFilterInputTypeDefinition typeDefinition,
        IFilterFieldDefinition fieldDefinition)
    {
        return new CustomFilterMetadata(fieldDefinition);
    }
}

// 3. Implement query builder
public class CustomQueryBuilder<T> : IQueryBuilder
{
    private readonly string _argumentName;
    private readonly CustomProvider _provider;
    
    public CustomQueryBuilder(string argumentName, CustomProvider provider)
    {
        _argumentName = argumentName;
        _provider = provider;
    }

    public void Prepare(IMiddlewareContext context)
    {
        var filterValue = context.ArgumentValue<IValueNode>(_argumentName);
        
        if (filterValue != null && !filterValue.IsNull())
        {
            var filterType = context.Field.Arguments[_argumentName].Type;
            var visitor = new CustomFilterVisitor(_provider.FieldHandlers);
            var providerContext = visitor.Visit(filterValue, filterType);
            
            context.SetLocalState("custom.context", providerContext);
        }
    }

    public void Apply(IMiddlewareContext context)
    {
        if (context.Result is ICustomQueryable<T> queryable &&
            context.GetLocalState<CustomProviderContext>("custom.context") is { } filterContext)
        {
            var filteredResult = queryable.ApplyFilter(filterContext.Query);
            context.Result = filteredResult;
        }
    }
}

// 4. Registration
services.AddGraphQLServer()
    .AddFiltering()
    .AddConvention<IFilterConvention>(new FilterConventionExtension(x => 
        x.Provider(new CustomProvider())));
```

This comprehensive guide provides the advanced patterns needed to implement sophisticated features in HotChocolate 15, covering complex type scenarios, advanced middleware patterns, and data provider integration architectures.