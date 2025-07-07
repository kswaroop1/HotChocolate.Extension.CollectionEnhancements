using HotChocolate.Execution;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;

public static class TestServerFactory
{
    private static readonly Lazy<Task<IRequestExecutor>> Executor = new(CreateExecutorAsync);

    public static IServiceProvider CreateTestServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISecurityService, TestSecurityService>();
        services.AddSingleton<ICustomerService, TestCustomerService>();
        services.AddSingleton<IPersonService, TestPersonService>();

        services
            .AddGraphQLServer()
            .AddQueryType<Query>()
            .ModifyRequestOptions(options => options.IncludeExceptionDetails = true)
            .AddFiltering()
            .AddSorting()
            .AddProjections()
            .AddCollectionEnhancements();

        return services.BuildServiceProvider();
    }

    public static Task<IRequestExecutor> CreateTestExecutorAsync() => Executor.Value;

    private static async Task<IRequestExecutor> CreateExecutorAsync()
    {
        var services = CreateTestServices();
        return await services.GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();
    }
}
