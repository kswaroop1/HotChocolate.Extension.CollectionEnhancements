using HotChocolate.Execution.Configuration;
using HotChocolate.Data;
using HotChocolate.Extension.CollectionEnhancements.Execution;
using HotChocolate.Extension.CollectionEnhancements.Metadata;
using HotChocolate.Extension.CollectionEnhancements.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Extension.CollectionEnhancements;

public static class RequestExecutorBuilderExtensions
{
    public static IRequestExecutorBuilder AddCollectionEnhancements(this IRequestExecutorBuilder builder)
    {
        var catalog = CollectionSchemaCatalog.CreateDefault();
        builder.Services.AddSingleton(catalog);
        builder.Services.AddSingleton<CollectionExecutionEngine>();
        builder.AddFiltering();
        builder.AddSorting();
        builder.Services.AddHttpResponseFormatter<CollectionEnhancementHttpResponseFormatter>();
        builder.AddHttpRequestInterceptor<CollectionEnhancementHttpRequestInterceptor>();

        var registrar = new CollectionEnhancementTypeRegistrar(catalog);
        registrar.Register(builder);

        return builder;
    }
}
