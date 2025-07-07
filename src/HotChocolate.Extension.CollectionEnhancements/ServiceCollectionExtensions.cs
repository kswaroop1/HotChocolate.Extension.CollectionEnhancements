using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Extension.CollectionEnhancements;

public static class RequestExecutorBuilderExtensions
{
    public static IRequestExecutorBuilder AddCollectionEnhancements(this IRequestExecutorBuilder builder)
    {
        // TODO: Implement the collection extensions registration.
        // This is the canonical entry point used as:
        // services.AddGraphQLServer().AddCollectionEnhancements()

        return builder;
    }
}
