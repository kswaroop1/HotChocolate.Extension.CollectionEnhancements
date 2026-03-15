using System.Reflection;
using System.Runtime.CompilerServices;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

internal static class ReflectionTestSupport
{
    public static object? InvokeStatic(Type declaringType, string methodName, params object?[]? args) =>
        GetMethod(declaringType, methodName, args, isStatic: true).Invoke(null, NormalizeArguments(args));

    public static object? InvokeInstance(object instance, string methodName, params object?[]? args) =>
        GetMethod(instance.GetType(), methodName, args, isStatic: false).Invoke(instance, NormalizeArguments(args));

    public static object? InvokeStaticGeneric(Type declaringType, string methodName, Type[] genericArguments, params object?[]? args) =>
        GetMethod(declaringType, methodName, args, isStatic: true)
            .MakeGenericMethod(genericArguments)
            .Invoke(null, NormalizeArguments(args));

    public static async Task<object?> InvokeStaticAsync(Type declaringType, string methodName, params object?[]? args) =>
        await AwaitAsync(InvokeStatic(declaringType, methodName, args));

    public static async Task<object?> InvokeStaticGenericAsync(Type declaringType, string methodName, Type[] genericArguments, params object?[]? args) =>
        await AwaitAsync(InvokeStaticGeneric(declaringType, methodName, genericArguments, args));

    public static async Task<object?> InvokeInstanceAsync(object instance, string methodName, params object?[]? args) =>
        await AwaitAsync(InvokeInstance(instance, methodName, args));

    public static T GetNestedPrivateType<T>(Type declaringType, string nestedTypeName, string propertyName)
        where T : class =>
        (T)declaringType.GetNestedType(nestedTypeName, BindingFlags.NonPublic)!
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    public static object Create(Type interfaceType, Func<MethodInfo, object?[]?, object?> handler)
    {
        var createMethod = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(DispatchProxy.Create) && method.IsGenericMethodDefinition)
            .MakeGenericMethod(interfaceType, typeof(DynamicDispatchProxy));

        var proxy = createMethod.Invoke(null, null)!;
        ((DynamicDispatchProxy)proxy).Handler = handler;
        return proxy;
    }

    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class =>
        (T)Create(typeof(T), handler);

    public static IResolverContext CreateResolverContext(
        FieldNode fieldNode,
        object? parent = null,
        IReadOnlyDictionary<string, object?>? arguments = null,
        IServiceProvider? services = null,
        string declaringTypeName = "Query",
        OperationDefinitionNode? operationDefinition = null,
        CancellationToken requestAborted = default)
    {
        var argumentValues = arguments ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        operationDefinition ??= new OperationDefinitionNode(
            null,
            null,
            OperationType.Query,
            [],
            [],
            new SelectionSetNode([fieldNode]));

        var selectionProperty = typeof(IResolverContext).GetProperty(nameof(IResolverContext.Selection))!;
        var selectionType = selectionProperty.PropertyType;
        var declaringTypeProperty = selectionType.GetProperty("DeclaringType")!;
        var declaringType = Create(
            declaringTypeProperty.PropertyType,
            (method, _) => method.Name switch
            {
                "get_Name" => declaringTypeName,
                _ => GetDefault(method.ReturnType)
            });

        var selection = Create(
            selectionType,
            (method, _) => method.Name switch
            {
                "get_SyntaxNode" => fieldNode,
                "get_DeclaringType" => declaringType,
                _ => GetDefault(method.ReturnType)
            });

        var operationType = typeof(IResolverContext).GetProperty(nameof(IResolverContext.Operation))!.PropertyType;
        var operation = Create(
            operationType,
            (method, _) => method.Name switch
            {
                "get_Definition" => operationDefinition,
                _ => GetDefault(method.ReturnType)
            });

        var serviceProvider = services ?? new ServiceCollection().BuildServiceProvider();

        return Create<IResolverContext>((method, methodArguments) => method.Name switch
        {
            "get_Selection" => selection,
            "get_Operation" => operation,
            "get_Services" => serviceProvider,
            "get_RequestAborted" => requestAborted,
            "Parent" => parent,
            "ArgumentValue" => argumentValues.TryGetValue((string)methodArguments![0]!, out var value)
                ? value
                : GetDefault(method.ReturnType),
            _ => GetDefault(method.ReturnType)
        });
    }

    private static MethodInfo GetMethod(Type declaringType, string methodName, object?[]? args, bool isStatic)
    {
        var candidates = declaringType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance))
            .Where(method => method.Name == methodName)
            .ToArray();

        var argCount = args?.Length ?? 0;
        return candidates.First(method => method.GetParameters().Length == argCount);
    }

    private static object?[] NormalizeArguments(object?[]? args) => args ?? [];

    private static async Task<object?> AwaitAsync(object? result)
    {
        if (result is null)
        {
            return null;
        }

        switch (result)
        {
            case Task task:
                await task;
                return GetTaskResult(task);
            case ValueTask valueTask:
                await valueTask;
                return null;
        }

        var resultType = result.GetType();
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var task = (Task)resultType.GetMethod(nameof(ValueTask<object>.AsTask))!.Invoke(result, null)!;
            await task;
            return GetTaskResult(task);
        }

        return result;
    }

    private static object? GetTaskResult(Task task)
    {
        var taskType = task.GetType();
        return taskType.IsGenericType
            ? taskType.GetProperty(nameof(Task<object>.Result))!.GetValue(task)
            : null;
    }

    private static object? GetDefault(Type type) =>
        type == typeof(void)
            ? null
            : type.IsValueType
                ? RuntimeHelpers.GetUninitializedObject(type)
                : null;

    private class DynamicDispatchProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args);
    }
}

[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class ParentAttribute : Attribute;

[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class ServiceAttribute : Attribute;

internal sealed class DummyService(string name)
{
    public string Name { get; } = name;
}

internal sealed class DummyHost(string name)
{
    public string Name { get; } = name;

    public string Field = "field-value";

    public string Property => "property-value";

    public string Method() => "method-value";

    public event EventHandler? Changed;

    public IReadOnlyList<int> GetNumbers(
        [Parent] DummyHost parent,
        [Service] DummyService service,
        CancellationToken cancellationToken,
        int limit = 5) =>
        [parent.Name.Length, service.Name.Length, cancellationToken.CanBeCanceled ? 1 : 0, limit];

    public Task<IReadOnlyList<int>> GetNumbersAsync([Service] DummyService service) =>
        Task.FromResult<IReadOnlyList<int>>([service.Name.Length]);

    public ValueTask<IReadOnlyList<int>> GetNumbersValueTask([Service] DummyService service) =>
        ValueTask.FromResult<IReadOnlyList<int>>([service.Name.Length]);

    public IReadOnlyList<int> GetNumbersWithoutDefault(int limit) => [limit];
}

internal abstract class AbstractCoverageType;

internal sealed class ThrowingAssembly : Assembly
{
    public override Type[] GetTypes() =>
        throw new ReflectionTypeLoadException([typeof(string), null!], []);

    public override AssemblyName GetName() => new("ThrowingAssembly");

    public override string FullName => "ThrowingAssembly";

    public override string Location => string.Empty;

    public override bool IsDynamic => false;
}

internal sealed class NullNameAssembly : Assembly
{
    public override Type[] GetTypes() => [typeof(string)];

    public override AssemblyName GetName() => new();

    public override string FullName => string.Empty;

    public override string Location => string.Empty;

    public override bool IsDynamic => false;
}
