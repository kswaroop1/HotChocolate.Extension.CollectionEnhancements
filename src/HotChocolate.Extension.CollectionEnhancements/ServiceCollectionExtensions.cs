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
    public static IRequestExecutorBuilder AddCollectionEnhancements(
        this IRequestExecutorBuilder builder,
        Action<CollectionEnhancementOptions>? configure = null)
    {
        var options = new CollectionEnhancementOptions();
        configure?.Invoke(options);

        var catalog = CollectionSchemaCatalog.CreateDefault();
        builder.Services.AddSingleton(catalog);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(sp => new CollectionExecutionEngine(
            sp.GetRequiredService<CollectionSchemaCatalog>(),
            sp.GetRequiredService<CollectionEnhancementOptions>()));
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
