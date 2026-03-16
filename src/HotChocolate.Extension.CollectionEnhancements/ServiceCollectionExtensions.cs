using HotChocolate.Execution.Configuration;
using HotChocolate.CostAnalysis;
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
        builder.AddCostAnalyzer()
            .ModifyCostOptions(options =>
            {
                options.MaxFieldCost = 500_000;
                options.MaxTypeCost = 500_000;
                options.EnforceCostLimits = false;
                options.ApplyCostDefaults = true;
                options.ApplySlicingArgumentDefaultValue = true;
            });
        builder.AddProjections();
        builder.AddFiltering();
        builder.AddSorting();
        builder.Services.AddHttpResponseFormatter<CollectionEnhancementHttpResponseFormatter>();
        builder.AddHttpRequestInterceptor<CollectionEnhancementHttpRequestInterceptor>();

        var registrar = new CollectionEnhancementTypeRegistrar(catalog);
        registrar.Register(builder);

        return builder;
    }
}
