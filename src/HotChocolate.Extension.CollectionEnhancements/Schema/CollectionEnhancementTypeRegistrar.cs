using System.Reflection;
using HotChocolate.Execution.Configuration;
using HotChocolate.Extension.CollectionEnhancements.Execution;
using HotChocolate.Extension.CollectionEnhancements.Metadata;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace HotChocolate.Extension.CollectionEnhancements.Schema;

internal sealed class CollectionEnhancementTypeRegistrar(CollectionSchemaCatalog catalog)
{
    private readonly CollectionSchemaCatalog _catalog = catalog;

    public void Register(IRequestExecutorBuilder builder)
    {
        RegisterSharedTypes(builder);

        foreach (var objectType in _catalog.ObjectTypes)
        {
            RegisterObjectFilterType(builder, objectType);
            RegisterObjectSortType(builder, objectType);
            RegisterObjectExtension(builder, objectType);

            foreach (var collectionField in objectType.CollectionFields)
            {
                RegisterCollectionTypes(builder, collectionField);
            }
        }
    }

    private void RegisterSharedTypes(IRequestExecutorBuilder builder)
    {
        builder.AddType<ExportFormatType>();
        builder.AddDirectiveType<ExportDirectiveType>();

        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name("CeStringOperationFilterInput");
            descriptor.Field("eq").Type(GraphQlTypeReferenceHelper.Parse("String"));
            descriptor.Field("neq").Type(GraphQlTypeReferenceHelper.Parse("String"));
            descriptor.Field("gt").Type(GraphQlTypeReferenceHelper.Parse("String"));
            descriptor.Field("gte").Type(GraphQlTypeReferenceHelper.Parse("String"));
            descriptor.Field("lt").Type(GraphQlTypeReferenceHelper.Parse("String"));
            descriptor.Field("lte").Type(GraphQlTypeReferenceHelper.Parse("String"));
            descriptor.Field("in").Type(GraphQlTypeReferenceHelper.Parse("[String!]"));
            descriptor.Field("nin").Type(GraphQlTypeReferenceHelper.Parse("[String!]"));
        }));

        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name("CeBooleanOperationFilterInput");
            descriptor.Field("eq").Type<BooleanType>();
            descriptor.Field("neq").Type<BooleanType>();
            descriptor.Field("in").Type(GraphQlTypeReferenceHelper.Parse("[Boolean!]"));
            descriptor.Field("nin").Type(GraphQlTypeReferenceHelper.Parse("[Boolean!]"));
        }));

        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name("CeFloatOperationFilterInput");
            descriptor.Field("eq").Type<FloatType>();
            descriptor.Field("neq").Type<FloatType>();
            descriptor.Field("gt").Type<FloatType>();
            descriptor.Field("gte").Type<FloatType>();
            descriptor.Field("lt").Type<FloatType>();
            descriptor.Field("lte").Type<FloatType>();
            descriptor.Field("in").Type(GraphQlTypeReferenceHelper.Parse("[Float!]"));
            descriptor.Field("nin").Type(GraphQlTypeReferenceHelper.Parse("[Float!]"));
        }));

        builder.AddType(new EnumType(descriptor =>
        {
            descriptor.Name("CollectionEnhancementSortDirection");
            descriptor.Value("ASC");
            descriptor.Value("DESC");
        }));

        foreach (var enumType in _catalog.ObjectTypes
            .SelectMany(type => type.ScalarFields)
            .Select(field => Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType)
            .Where(type => type.IsEnum)
            .Distinct())
        {
            RegisterEnumOperationFilterType(builder, enumType);
        }
    }

    private static void RegisterEnumOperationFilterType(IRequestExecutorBuilder builder, Type enumType)
    {
        var enumTypeName = GraphQlNaming.GetTypeName(enumType);
        var operationTypeName = $"Ce{enumTypeName}OperationFilterInput";

        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(operationTypeName);
            descriptor.Field("eq").Type(GraphQlTypeReferenceHelper.Parse(enumTypeName));
            descriptor.Field("neq").Type(GraphQlTypeReferenceHelper.Parse(enumTypeName));
            descriptor.Field("in").Type(GraphQlTypeReferenceHelper.Parse($"[{enumTypeName}!]"));
            descriptor.Field("nin").Type(GraphQlTypeReferenceHelper.Parse($"[{enumTypeName}!]"));
        }));
    }

    private void RegisterObjectFilterType(IRequestExecutorBuilder builder, ObjectTypeModel objectType)
    {
        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(objectType.FilterInputName);
            descriptor.Field("and").Type(GraphQlTypeReferenceHelper.Parse($"[{objectType.FilterInputName}!]"));
            descriptor.Field("or").Type(GraphQlTypeReferenceHelper.Parse($"[{objectType.FilterInputName}!]"));
            descriptor.Field("not").Type(GraphQlTypeReferenceHelper.Parse(objectType.FilterInputName));

            foreach (var scalarField in objectType.ScalarFields)
            {
                descriptor.Field(scalarField.GraphQlName)
                    .Type(GraphQlTypeReferenceHelper.Parse(GraphQlTypeReferenceHelper.GetOperationFilterTypeName(scalarField.ClrType)));
            }

            foreach (var objectField in objectType.ObjectFields)
            {
                if (_catalog.TryGetObjectType(objectField.ClrType) is { } nestedModel)
                {
                    descriptor.Field(objectField.GraphQlName)
                        .Type(GraphQlTypeReferenceHelper.Parse(nestedModel.FilterInputName));
                }
            }

            foreach (var collectionField in objectType.CollectionFields)
            {
                descriptor.Field(collectionField.AggregateFieldName)
                    .Type(GraphQlTypeReferenceHelper.Parse(collectionField.AggregateCriteriaInputName));

                descriptor.Field(collectionField.GroupFieldName)
                    .Type(GraphQlTypeReferenceHelper.Parse(collectionField.GroupCriteriaInputName));
            }
        }));
    }

    private void RegisterObjectSortType(IRequestExecutorBuilder builder, ObjectTypeModel objectType)
    {
        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(objectType.SortInputName);

            foreach (var scalarField in objectType.ScalarFields)
            {
                descriptor.Field(scalarField.GraphQlName).Type(GraphQlTypeReferenceHelper.Parse("CollectionEnhancementSortDirection"));
            }
        }));
    }

    private void RegisterCollectionTypes(IRequestExecutorBuilder builder, CollectionFieldModel collectionField)
    {
        RegisterAggregateCriteriaInputs(builder, collectionField);
        RegisterGroupCriteriaInputs(builder, collectionField);
        RegisterAggregateResultTypes(builder, collectionField, isFlat: false);
        RegisterGroupResultTypes(builder, collectionField, isFlat: false);
        RegisterFlatTypes(builder, collectionField);
    }

    private void RegisterAggregateCriteriaInputs(IRequestExecutorBuilder builder, CollectionFieldModel collectionField)
    {
        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(collectionField.AggregateCriteriaInputName);
            descriptor.Field("where").Type(GraphQlTypeReferenceHelper.Parse(collectionField.FilterInputName));
            descriptor.Field("having").Type(GraphQlTypeReferenceHelper.Parse(collectionField.AggregateHavingInputName));
        }));
    }

    private void RegisterGroupCriteriaInputs(IRequestExecutorBuilder builder, CollectionFieldModel collectionField)
    {
        RegisterGroupByEnum(builder, collectionField.GroupByEnumName, GetBaseScalarFields(collectionField));

        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(collectionField.GroupCriteriaInputName);
            descriptor.Field("by").Type(GraphQlTypeReferenceHelper.Parse($"[{collectionField.GroupByEnumName}!]"));
            descriptor.Field("where").Type(GraphQlTypeReferenceHelper.Parse(collectionField.FilterInputName));
            descriptor.Field("having").Type(GraphQlTypeReferenceHelper.Parse(collectionField.GroupHavingInputName));
        }));
    }

    private void RegisterAggregateResultTypes(IRequestExecutorBuilder builder, CollectionFieldModel collectionField, bool isFlat)
    {
        var scalarFields = isFlat
            ? GetFlatScalarFields(collectionField)
            : GetBaseScalarFields(collectionField);

        var resultName = isFlat ? collectionField.FlatAggregateResultName : collectionField.AggregateResultName;
        var havingInputName = isFlat ? collectionField.FlatAggregateHavingInputName : collectionField.AggregateHavingInputName;
        var filterInputName = isFlat ? collectionField.FlatRowFilterInputName : collectionField.FilterInputName;

        RegisterOperatorResultTypes(builder, collectionField, isFlat, scalarFields);
        RegisterAggregateHavingDetails(builder, collectionField, isFlat, scalarFields);

        builder.AddType(new ObjectType(descriptor =>
        {
            descriptor.Name(resultName);
            descriptor.Field("count")
                .Type<NonNullType<IntType>>()
                .Argument("where", argument => argument.Type(GraphQlTypeReferenceHelper.Parse(filterInputName)))
                .Resolve(context =>
                {
                    var engine = context.Service<CollectionExecutionEngine>();
                    var selection = context.Parent<AggregateSelectionContext>();
                    var where = ResolverArgumentReader.GetOptionalArgument(context, "where");
                    return engine.ResolveCount(selection, where);
                });

            ConfigureAggregateOperatorFields(descriptor, collectionField, scalarFields, isFlat, includeKey: false);
        }));

        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(havingInputName);
            descriptor.Field("and").Type(GraphQlTypeReferenceHelper.Parse($"[{havingInputName}!]"));
            descriptor.Field("or").Type(GraphQlTypeReferenceHelper.Parse($"[{havingInputName}!]"));
            descriptor.Field("not").Type(GraphQlTypeReferenceHelper.Parse(havingInputName));
            descriptor.Field("count").Type(GraphQlTypeReferenceHelper.Parse("CeFloatOperationFilterInput"));
            ConfigureHavingOperatorFields(descriptor, collectionField, scalarFields, isFlat);
        }));
    }

    private void RegisterGroupResultTypes(IRequestExecutorBuilder builder, CollectionFieldModel collectionField, bool isFlat)
    {
        var scalarFields = isFlat
            ? GetFlatScalarFields(collectionField)
            : GetBaseScalarFields(collectionField);

        var groupRowName = isFlat ? collectionField.FlatGroupRowName : collectionField.GroupRowName;
        var keyName = isFlat ? collectionField.FlatGroupKeyName : collectionField.GroupKeyName;
        var keyOrderInputName = $"{keyName}OrderInput";
        var groupOrderInputName = isFlat ? collectionField.FlatGroupOrderInputName : collectionField.GroupOrderInputName;
        var groupHavingInputName = isFlat ? collectionField.FlatGroupHavingInputName : collectionField.GroupHavingInputName;

        if (isFlat)
        {
            RegisterGroupByEnum(builder, collectionField.FlatGroupByEnumName, scalarFields);
        }

        RegisterOperatorOrderTypes(builder, collectionField, isFlat, scalarFields);

        builder.AddType(new ObjectType(descriptor =>
        {
            descriptor.Name(keyName);

            foreach (var scalarField in scalarFields)
            {
                descriptor.Field(scalarField.FieldName)
                    .Type(GraphQlTypeReferenceHelper.Parse(GraphQlTypeReferenceHelper.GetOptionalScalarTypeSyntax(scalarField.ClrType)))
                    .Resolve(context =>
                    {
                        var key = context.Parent<IReadOnlyDictionary<string, object?>>();
                        return key.GetValueOrDefault(scalarField.FieldName);
                    });
            }
        }));

        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(keyOrderInputName);

            foreach (var scalarField in scalarFields)
            {
                descriptor.Field(scalarField.FieldName).Type(GraphQlTypeReferenceHelper.Parse("CollectionEnhancementSortDirection"));
            }
        }));

        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(groupOrderInputName);
            descriptor.Field("key").Type(GraphQlTypeReferenceHelper.Parse(keyOrderInputName));
            descriptor.Field("count").Type(GraphQlTypeReferenceHelper.Parse("CollectionEnhancementSortDirection"));
            ConfigureOperatorOrderFields(descriptor, collectionField, scalarFields, isFlat);
        }));

        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(groupHavingInputName);
            descriptor.Field("and").Type(GraphQlTypeReferenceHelper.Parse($"[{groupHavingInputName}!]"));
            descriptor.Field("or").Type(GraphQlTypeReferenceHelper.Parse($"[{groupHavingInputName}!]"));
            descriptor.Field("not").Type(GraphQlTypeReferenceHelper.Parse(groupHavingInputName));
            descriptor.Field("count").Type(GraphQlTypeReferenceHelper.Parse("CeFloatOperationFilterInput"));
            ConfigureHavingOperatorFields(descriptor, collectionField, scalarFields, isFlat);
        }));

        builder.AddType(new ObjectType(descriptor =>
        {
            descriptor.Name(groupRowName);
            descriptor.Field("key")
                .Type(GraphQlTypeReferenceHelper.Parse(keyName))
                .Resolve(context => context.Parent<GroupRowResult>().Key);

            descriptor.Field("count")
                .Type<NonNullType<IntType>>()
                .Argument("where", argument => argument.Type(GraphQlTypeReferenceHelper.Parse(isFlat ? collectionField.FlatRowFilterInputName : collectionField.FilterInputName)))
                .Resolve(context =>
                {
                    var engine = context.Service<CollectionExecutionEngine>();
                    var row = context.Parent<GroupRowResult>();
                    return engine.ResolveCount(row.Selection, ResolverArgumentReader.GetOptionalArgument(context, "where"));
                });

            ConfigureAggregateOperatorFields(descriptor, collectionField, scalarFields, isFlat, includeKey: true);
        }));
    }

    private void RegisterFlatTypes(IRequestExecutorBuilder builder, CollectionFieldModel collectionField)
    {
        var scalarFields = GetFlatScalarFields(collectionField);

        RegisterAggregateResultTypes(builder, collectionField, isFlat: true);
        RegisterGroupResultTypes(builder, collectionField, isFlat: true);

        builder.AddType(new ObjectType(descriptor =>
        {
            descriptor.Name(collectionField.FlatRowName);

            foreach (var scalarField in scalarFields)
            {
                descriptor.Field(scalarField.FieldName)
                    .Type(GraphQlTypeReferenceHelper.Parse(GraphQlTypeReferenceHelper.GetOptionalScalarTypeSyntax(scalarField.ClrType)))
                    .Resolve(context => context.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(scalarField.FieldName));
            }
        }));

        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(collectionField.FlatRowFilterInputName);
            descriptor.Field("and").Type(GraphQlTypeReferenceHelper.Parse($"[{collectionField.FlatRowFilterInputName}!]"));
            descriptor.Field("or").Type(GraphQlTypeReferenceHelper.Parse($"[{collectionField.FlatRowFilterInputName}!]"));
            descriptor.Field("not").Type(GraphQlTypeReferenceHelper.Parse(collectionField.FlatRowFilterInputName));

            foreach (var scalarField in scalarFields)
            {
                descriptor.Field(scalarField.FieldName)
                    .Type(GraphQlTypeReferenceHelper.Parse(GraphQlTypeReferenceHelper.GetOperationFilterTypeName(scalarField.ClrType)));
            }
        }));

        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(collectionField.FlatRowSortInputName);

            foreach (var scalarField in scalarFields)
            {
                descriptor.Field(scalarField.FieldName).Type(GraphQlTypeReferenceHelper.Parse("CollectionEnhancementSortDirection"));
            }
        }));
    }

    private static void RegisterGroupByEnum(IRequestExecutorBuilder builder, string name, IReadOnlyList<FlatScalarFieldDefinition> fields)
    {
        builder.AddType(new EnumType(descriptor =>
        {
            descriptor.Name(name);

            foreach (var field in fields)
            {
                descriptor.Value(field.FieldName).Name(field.FieldName);
            }
        }));
    }

    private void RegisterObjectExtension(IRequestExecutorBuilder builder, ObjectTypeModel objectType)
    {
        var method = GetType()
            .GetMethod(nameof(RegisterObjectExtensionCore), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(objectType.ClrType);

        method.Invoke(this, [builder, objectType]);
    }

    private void RegisterObjectExtensionCore<THost>(IRequestExecutorBuilder builder, ObjectTypeModel objectType)
    {
        builder.AddObjectTypeExtension<THost>(objectType.GraphQlTypeName, descriptor =>
        {
            descriptor.ExtendsType<THost>();

            foreach (var collectionField in objectType.CollectionFields)
            {
                ConfigureCollectionField(descriptor.Field(collectionField.GraphQlName), collectionField);
                ConfigureAggregateField(CreateSiblingField<THost>(descriptor, collectionField.Member, collectionField.AggregateFieldName), collectionField, isFlat: false);
                ConfigureGroupField(CreateSiblingField<THost>(descriptor, collectionField.Member, collectionField.GroupFieldName), collectionField, isFlat: false);
                ConfigureFlatField(CreateSiblingField<THost>(descriptor, collectionField.Member, collectionField.FlatFieldName), collectionField);
                ConfigureAggregateField(CreateSiblingField<THost>(descriptor, collectionField.Member, collectionField.FlatAggregateFieldName), collectionField, isFlat: true);
                ConfigureGroupField(CreateSiblingField<THost>(descriptor, collectionField.Member, collectionField.FlatGroupFieldName), collectionField, isFlat: true);
            }
        });
    }

    private static void ConfigureCollectionField(IObjectFieldDescriptor field, CollectionFieldModel collectionField)
    {
        field.Argument("where", argument => argument.Type(GraphQlTypeReferenceHelper.Parse(collectionField.FilterInputName)))
            .Argument("order", argument => argument.Type(GraphQlTypeReferenceHelper.Parse($"[{collectionField.SortInputName}!]")))
            .Argument("offset", argument => argument.Type<IntType>())
            .Argument("limit", argument => argument.Type<IntType>());

        field.Use(next => async context =>
        {
            await next(context);

            var engine = context.Service<CollectionExecutionEngine>();
            context.Result = engine.ApplyCollectionArgumentsForField(
                collectionField,
                context.Result,
                ResolverArgumentReader.GetOptionalArgument(context, "where"),
                ResolverArgumentReader.GetOptionalArgument(context, "order"),
                ResolverArgumentReader.GetNullableInt(context, "offset"),
                ResolverArgumentReader.GetNullableInt(context, "limit"));
        });
    }

    private static void ConfigureAggregateField(IObjectFieldDescriptor field, CollectionFieldModel collectionField, bool isFlat)
    {
        field.Argument("where", argument => argument.Type(GraphQlTypeReferenceHelper.Parse(isFlat ? collectionField.FlatRowFilterInputName : collectionField.FilterInputName)))
            .Argument("having", argument => argument.Type(GraphQlTypeReferenceHelper.Parse(isFlat ? collectionField.FlatAggregateHavingInputName : collectionField.AggregateHavingInputName)))
            .Type(GraphQlTypeReferenceHelper.Parse(isFlat ? collectionField.FlatAggregateResultName : collectionField.AggregateResultName));

        if (isFlat)
        {
            field.Argument("expand", argument => argument.Type(GraphQlTypeReferenceHelper.Parse("[String!]!")));
        }

        field.Use(next => async context =>
        {
            if (isFlat)
            {
                ValidateFlatReferences(collectionField, ResolverArgumentReader.GetStringList(context, "expand"), context);
            }

            ValidateExportDirective(context, ExportTargetKind.FlatAggregate);

            await next(context);

            var engine = context.Service<CollectionExecutionEngine>();
            var expand = isFlat
                ? ResolverArgumentReader.GetStringList(context, "expand")
                : null;

            context.Result = engine.CreateAggregateSelection(
                collectionField,
                context.Result,
                ResolverArgumentReader.GetOptionalArgument(context, "where"),
                ResolverArgumentReader.GetOptionalArgument(context, "having"),
                isFlat,
                expand);
        });
    }

    private static void ConfigureGroupField(IObjectFieldDescriptor field, CollectionFieldModel collectionField, bool isFlat)
    {
        field.Argument("by", argument => argument.Type(GraphQlTypeReferenceHelper.Parse($"[{(isFlat ? collectionField.FlatGroupByEnumName : collectionField.GroupByEnumName)}!]")))
            .Argument("where", argument => argument.Type(GraphQlTypeReferenceHelper.Parse(isFlat ? collectionField.FlatRowFilterInputName : collectionField.FilterInputName)))
            .Argument("having", argument => argument.Type(GraphQlTypeReferenceHelper.Parse(isFlat ? collectionField.FlatGroupHavingInputName : collectionField.GroupHavingInputName)))
            .Argument("order", argument => argument.Type(GraphQlTypeReferenceHelper.Parse($"[{(isFlat ? collectionField.FlatGroupOrderInputName : collectionField.GroupOrderInputName)}!]")))
            .Argument("offset", argument => argument.Type<IntType>())
            .Argument("limit", argument => argument.Type<IntType>())
            .Type(GraphQlTypeReferenceHelper.Parse($"[{(isFlat ? collectionField.FlatGroupRowName : collectionField.GroupRowName)}!]"));

        if (isFlat)
        {
            field.Argument("expand", argument => argument.Type(GraphQlTypeReferenceHelper.Parse("[String!]!")));
        }

        field.Use(next => async context =>
        {
            if (isFlat)
            {
                ValidateFlatReferences(collectionField, ResolverArgumentReader.GetStringList(context, "expand"), context, includeByArgument: true);
            }

            ValidateExportDirective(context, isFlat ? ExportTargetKind.FlatGroup : ExportTargetKind.None);

            await next(context);

            var engine = context.Service<CollectionExecutionEngine>();
            var expand = isFlat
                ? ResolverArgumentReader.GetStringList(context, "expand")
                : null;

            context.Result = engine.CreateGroupRows(
                collectionField,
                context.Result,
                ResolverArgumentReader.GetOptionalArgument(context, "by"),
                ResolverArgumentReader.GetOptionalArgument(context, "where"),
                ResolverArgumentReader.GetOptionalArgument(context, "having"),
                ResolverArgumentReader.GetOptionalArgument(context, "order"),
                ResolverArgumentReader.GetNullableInt(context, "offset"),
                ResolverArgumentReader.GetNullableInt(context, "limit"),
                isFlat,
                expand);
        });
    }

    private static void ConfigureFlatField(IObjectFieldDescriptor field, CollectionFieldModel collectionField)
    {
        field.Argument("expand", argument => argument.Type(GraphQlTypeReferenceHelper.Parse("[String!]!")))
            .Argument("where", argument => argument.Type(GraphQlTypeReferenceHelper.Parse(collectionField.FlatRowFilterInputName)))
            .Argument("order", argument => argument.Type(GraphQlTypeReferenceHelper.Parse($"[{collectionField.FlatRowSortInputName}!]")))
            .Argument("offset", argument => argument.Type<IntType>())
            .Argument("limit", argument => argument.Type<IntType>())
            .Argument("maxDepth", argument => argument.Type<IntType>())
            .Type(GraphQlTypeReferenceHelper.Parse($"[{collectionField.FlatRowName}!]"));

        field.Use(next => async context =>
        {
            ValidateFlatReferences(collectionField, ResolverArgumentReader.GetStringList(context, "expand"), context);
            ValidateExportDirective(context, ExportTargetKind.Flat);

            await next(context);

            var engine = context.Service<CollectionExecutionEngine>();
            var expand = ResolverArgumentReader.GetStringList(context, "expand");

            context.Result = engine.ApplyFlatArguments(
                collectionField,
                context.Result,
                expand,
                ResolverArgumentReader.GetOptionalArgument(context, "where"),
                ResolverArgumentReader.GetOptionalArgument(context, "order"),
                ResolverArgumentReader.GetNullableInt(context, "offset"),
                ResolverArgumentReader.GetNullableInt(context, "limit"));
        });
    }

    private IReadOnlyList<FlatScalarFieldDefinition> GetBaseScalarFields(CollectionFieldModel collectionField) =>
        _catalog.TryGetObjectType(collectionField.ElementType)!.ScalarFields
            .Select(field => new FlatScalarFieldDefinition(field.GraphQlName, field.ClrType, FlatScalarFieldKind.Base))
            .ToArray();

    private IReadOnlyList<FlatScalarFieldDefinition> GetFlatScalarFields(CollectionFieldModel collectionField)
    {
        var fields = new List<FlatScalarFieldDefinition>(GetBaseScalarFields(collectionField));
        var flatShape = FlatRowShapeCache.GetOrCreate(collectionField);

        foreach (var (fieldName, path) in flatShape.GeneratedFieldOwners)
        {
            var property = path.TerminalElementType
                .GetProperties()
                .First(candidate => fieldName == path.Prefix + GraphQlNaming.ToPascalCase(GraphQlNaming.GetFieldName(candidate)));

            fields.Add(new FlatScalarFieldDefinition(fieldName, property.PropertyType, FlatScalarFieldKind.Generated));
        }

        return fields;
    }

    private static void ConfigureAggregateOperatorFields(
        IObjectTypeDescriptor descriptor,
        CollectionFieldModel collectionField,
        IReadOnlyList<FlatScalarFieldDefinition> scalarFields,
        bool isFlat,
        bool includeKey)
    {
        descriptor.Field("countDistinct")
            .Type(GraphQlTypeReferenceHelper.Parse(GetOperatorResultTypeName(collectionField, isFlat, AggregateOperator.CountDistinct)))
            .Resolve(context => new AggregateProjection(GetSelection(context), AggregateOperator.CountDistinct, null, null));

        ConfigureProjectionField(descriptor, "sum", collectionField, scalarFields, isFlat, AggregateOperator.Sum);
        ConfigureProjectionField(descriptor, "avg", collectionField, scalarFields, isFlat, AggregateOperator.Avg);
        ConfigureProjectionField(descriptor, "min", collectionField, scalarFields, isFlat, AggregateOperator.Min);
        ConfigureProjectionField(descriptor, "max", collectionField, scalarFields, isFlat, AggregateOperator.Max);
        ConfigureProjectionField(descriptor, "stdev", collectionField, scalarFields, isFlat, AggregateOperator.Stdev);
        ConfigureProjectionField(descriptor, "stdevp", collectionField, scalarFields, isFlat, AggregateOperator.Stdevp);
        ConfigureProjectionField(descriptor, "skew", collectionField, scalarFields, isFlat, AggregateOperator.Skew);
        ConfigureProjectionField(descriptor, "kurtosis", collectionField, scalarFields, isFlat, AggregateOperator.Kurtosis);

        if (GetApplicableFields(scalarFields, AggregateOperator.StringAgg).Count > 0)
        {
            descriptor.Field("stringAgg")
                .Type(GraphQlTypeReferenceHelper.Parse(GetOperatorResultTypeName(collectionField, isFlat, AggregateOperator.StringAgg)))
                .Argument("separator", argument => argument.Type<NonNullType<StringType>>())
                .Argument("order", argument => argument.Type(GraphQlTypeReferenceHelper.Parse($"[{(isFlat ? collectionField.FlatRowSortInputName : collectionField.SortInputName)}!]")))
                .Resolve(context => new AggregateProjection(
                    GetSelection(context),
                    AggregateOperator.StringAgg,
                    ResolverArgumentReader.GetRequiredString(context, "separator"),
                    ParseOrderArgument(ResolverArgumentReader.GetOptionalArgument(context, "order"))));
        }

        if (GetApplicableFields(scalarFields, AggregateOperator.StringAggDistinct).Count > 0)
        {
            descriptor.Field("stringAggDistinct")
                .Type(GraphQlTypeReferenceHelper.Parse(GetOperatorResultTypeName(collectionField, isFlat, AggregateOperator.StringAggDistinct)))
                .Argument("separator", argument => argument.Type<NonNullType<StringType>>())
                .Argument("order", argument => argument.Type(GraphQlTypeReferenceHelper.Parse($"[{(isFlat ? collectionField.FlatRowSortInputName : collectionField.SortInputName)}!]")))
                .Resolve(context => new AggregateProjection(
                    GetSelection(context),
                    AggregateOperator.StringAggDistinct,
                    ResolverArgumentReader.GetRequiredString(context, "separator"),
                    ParseOrderArgument(ResolverArgumentReader.GetOptionalArgument(context, "order"))));
        }
    }

    private void RegisterOperatorResultTypes(
        IRequestExecutorBuilder builder,
        CollectionFieldModel collectionField,
        bool isFlat,
        IReadOnlyList<FlatScalarFieldDefinition> scalarFields)
    {
        RegisterOperatorResultType(builder, collectionField, isFlat, scalarFields, AggregateOperator.CountDistinct);
        RegisterOperatorResultType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Sum);
        RegisterOperatorResultType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Avg);
        RegisterOperatorResultType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Min);
        RegisterOperatorResultType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Max);
        RegisterOperatorResultType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Stdev);
        RegisterOperatorResultType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Stdevp);
        RegisterOperatorResultType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Skew);
        RegisterOperatorResultType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Kurtosis);
        RegisterOperatorResultType(builder, collectionField, isFlat, scalarFields, AggregateOperator.StringAgg);
        RegisterOperatorResultType(builder, collectionField, isFlat, scalarFields, AggregateOperator.StringAggDistinct);
    }

    private void RegisterAggregateHavingDetails(
        IRequestExecutorBuilder builder,
        CollectionFieldModel collectionField,
        bool isFlat,
        IReadOnlyList<FlatScalarFieldDefinition> scalarFields)
    {
    }

    private static void ConfigureHavingOperatorFields(
        IInputObjectTypeDescriptor descriptor,
        CollectionFieldModel collectionField,
        IReadOnlyList<FlatScalarFieldDefinition> scalarFields,
        bool isFlat)
    {
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.CountDistinct, "HavingInput", "countDistinct");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Sum, "HavingInput", "sum");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Avg, "HavingInput", "avg");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Min, "HavingInput", "min");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Max, "HavingInput", "max");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Stdev, "HavingInput", "stdev");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Stdevp, "HavingInput", "stdevp");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Skew, "HavingInput", "skew");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Kurtosis, "HavingInput", "kurtosis");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.StringAgg, "HavingInput", "stringAgg");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.StringAggDistinct, "HavingInput", "stringAggDistinct");
    }

    private static void ConfigureOperatorOrderFields(
        IInputObjectTypeDescriptor descriptor,
        CollectionFieldModel collectionField,
        IReadOnlyList<FlatScalarFieldDefinition> scalarFields,
        bool isFlat)
    {
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.CountDistinct, "OrderInput", "countDistinct");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Sum, "OrderInput", "sum");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Avg, "OrderInput", "avg");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Min, "OrderInput", "min");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Max, "OrderInput", "max");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Stdev, "OrderInput", "stdev");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Stdevp, "OrderInput", "stdevp");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Skew, "OrderInput", "skew");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.Kurtosis, "OrderInput", "kurtosis");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.StringAgg, "OrderInput", "stringAgg");
        ConfigureOperatorInputField(descriptor, collectionField, scalarFields, isFlat, AggregateOperator.StringAggDistinct, "OrderInput", "stringAggDistinct");
    }

    private static void ConfigureOperatorInputField(
        IInputObjectTypeDescriptor descriptor,
        CollectionFieldModel collectionField,
        IReadOnlyList<FlatScalarFieldDefinition> scalarFields,
        bool isFlat,
        AggregateOperator @operator,
        string suffix,
        string fieldName)
    {
        if (GetApplicableFields(scalarFields, @operator).Count == 0)
        {
            return;
        }

        descriptor.Field(fieldName).Type(GraphQlTypeReferenceHelper.Parse(GetOperatorInputTypeName(collectionField, isFlat, @operator, suffix)));
    }

    private void RegisterOperatorOrderTypes(
        IRequestExecutorBuilder builder,
        CollectionFieldModel collectionField,
        bool isFlat,
        IReadOnlyList<FlatScalarFieldDefinition> scalarFields)
    {
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.CountDistinct, "OrderInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Sum, "OrderInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Avg, "OrderInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Min, "OrderInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Max, "OrderInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Stdev, "OrderInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Stdevp, "OrderInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Skew, "OrderInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Kurtosis, "OrderInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.StringAgg, "OrderInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.StringAggDistinct, "OrderInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.CountDistinct, "HavingInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Sum, "HavingInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Avg, "HavingInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Min, "HavingInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Max, "HavingInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Stdev, "HavingInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Stdevp, "HavingInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Skew, "HavingInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.Kurtosis, "HavingInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.StringAgg, "HavingInput");
        RegisterOperatorInputType(builder, collectionField, isFlat, scalarFields, AggregateOperator.StringAggDistinct, "HavingInput");
    }

    private void RegisterOperatorInputType(
        IRequestExecutorBuilder builder,
        CollectionFieldModel collectionField,
        bool isFlat,
        IReadOnlyList<FlatScalarFieldDefinition> scalarFields,
        AggregateOperator @operator,
        string suffix)
    {
        var applicableFields = GetApplicableFields(scalarFields, @operator);
        if (applicableFields.Count == 0)
        {
            return;
        }

        var inputTypeName = GetOperatorInputTypeName(collectionField, isFlat, @operator, suffix);
        builder.AddType(new InputObjectType(descriptor =>
        {
            descriptor.Name(inputTypeName);

            foreach (var field in applicableFields)
            {
                var fieldDescriptor = descriptor.Field(field.FieldName);

                if (suffix == "OrderInput")
                {
                    fieldDescriptor.Type(GraphQlTypeReferenceHelper.Parse("CollectionEnhancementSortDirection"));
                }
                else
                {
                    fieldDescriptor.Type(GraphQlTypeReferenceHelper.Parse(GetHavingOperationFilterType(@operator, field)));
                }
            }
        }));
    }

    private static string GetHavingOperationFilterType(AggregateOperator @operator, FlatScalarFieldDefinition field) =>
        @operator switch
        {
            AggregateOperator.CountDistinct => "CeFloatOperationFilterInput",
            AggregateOperator.Sum or AggregateOperator.Avg or AggregateOperator.Stdev or AggregateOperator.Stdevp or AggregateOperator.Skew or AggregateOperator.Kurtosis => "CeFloatOperationFilterInput",
            AggregateOperator.StringAgg or AggregateOperator.StringAggDistinct => "CeStringOperationFilterInput",
            _ => GraphQlTypeReferenceHelper.GetOperationFilterTypeName(field.ClrType)
        };

    private void RegisterOperatorResultType(
        IRequestExecutorBuilder builder,
        CollectionFieldModel collectionField,
        bool isFlat,
        IReadOnlyList<FlatScalarFieldDefinition> scalarFields,
        AggregateOperator @operator)
    {
        var applicableFields = GetApplicableFields(scalarFields, @operator);
        if (applicableFields.Count == 0)
        {
            return;
        }

        builder.AddType(new ObjectType(descriptor =>
        {
            descriptor.Name(GetOperatorResultTypeName(collectionField, isFlat, @operator));

            foreach (var field in applicableFields)
            {
                descriptor.Field(field.FieldName)
                    .Type(GraphQlTypeReferenceHelper.Parse(GetOperatorOutputType(@operator, field)))
                    .Resolve(context =>
                    {
                        var engine = context.Service<CollectionExecutionEngine>();
                        var projection = context.Parent<AggregateProjection>();
                        return engine.ResolveAggregateProjectionField(projection, field.FieldName);
                    });
            }
        }));
    }

    private static void ConfigureProjectionField(
        IObjectTypeDescriptor descriptor,
        string fieldName,
        CollectionFieldModel collectionField,
        IReadOnlyList<FlatScalarFieldDefinition> scalarFields,
        bool isFlat,
        AggregateOperator @operator)
    {
        if (GetApplicableFields(scalarFields, @operator).Count == 0)
        {
            return;
        }

        descriptor.Field(fieldName)
            .Type(GraphQlTypeReferenceHelper.Parse(GetOperatorResultTypeName(collectionField, isFlat, @operator)))
            .Resolve(context => new AggregateProjection(GetSelection(context), @operator, null, null));
    }

    private static AggregateSelectionContext GetSelection(IResolverContext context) =>
        context.Parent<object>() switch
        {
            AggregateSelectionContext selection => selection,
            GroupRowResult groupRow => groupRow.Selection,
            _ => throw new InvalidOperationException("Unexpected aggregate parent context.")
        };

    private static void ValidateFlatReferences(
        CollectionFieldModel collectionField,
        IReadOnlyList<string> expand,
        IResolverContext context,
        bool includeByArgument = false)
    {
        var flatShape = FlatRowShapeCache.GetOrCreate(collectionField);
        var allowedGeneratedFields = flatShape.GeneratedFieldOwners
            .Where(kvp => expand.Contains(kvp.Value.Path, StringComparer.Ordinal))
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.Ordinal);

        var referencedFields = CollectSelectionFieldReferences(context.Selection.SyntaxNode.SelectionSet);
        if (includeByArgument)
        {
            referencedFields = referencedFields
                .Concat(InputValueNormalizer.AsList(ResolverArgumentReader.GetOptionalArgument(context, "by"))
                    .Select(item => item?.ToString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Cast<string>());
        }

        var invalidFields = referencedFields
            .Where(field => flatShape.GeneratedFieldOwners.ContainsKey(field) && !allowedGeneratedFields.Contains(field))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (invalidFields.Length > 0)
        {
            throw new InvalidOperationException(
                $"The requested flat rowset does not include generated fields: {string.Join(", ", invalidFields)}.");
        }
    }

    private static IEnumerable<string> CollectSelectionFieldReferences(SelectionSetNode? selectionSet)
    {
        if (selectionSet is null)
        {
            yield break;
        }

        foreach (var selection in selectionSet.Selections.OfType<FieldNode>())
        {
            yield return selection.Name.Value;

            foreach (var nested in CollectSelectionFieldReferences(selection.SelectionSet))
            {
                yield return nested;
            }
        }
    }

    private static void ValidateExportDirective(IResolverContext context, ExportTargetKind targetKind)
    {
        var directive = context.Selection.SyntaxNode.Directives.FirstOrDefault(candidate => candidate.Name.Value == "export");
        if (directive is null)
        {
            return;
        }

        if (targetKind is not (ExportTargetKind.Flat or ExportTargetKind.FlatGroup))
        {
            throw new InvalidOperationException("The @export directive is only supported on root flat or root flat-group fields.");
        }

        if (!string.Equals(context.Selection.DeclaringType.Name, "Query", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The @export directive is only supported on root query fields.");
        }

        var rootFields = context.Operation.Definition.SelectionSet.Selections
            .OfType<FieldNode>()
            .ToArray();

        if (rootFields.Length != 1)
        {
            throw new InvalidOperationException("An export operation must select exactly one root field.");
        }

        var separator = ResolverArgumentReader.GetDirectiveArgument(directive, "separator")?.ToString() ?? ",";
        if (separator.Length != 1)
        {
            throw new InvalidOperationException("The export separator must be a single character.");
        }

        var duplicateHeaders = CollectLeafHeaders(context.Selection.SyntaxNode.SelectionSet)
            .GroupBy(header => header, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateHeaders.Length > 0)
        {
            throw new InvalidOperationException(
                $"The export selection produces duplicate headers: {string.Join(", ", duplicateHeaders)}.");
        }
    }

    private static IEnumerable<string> CollectLeafHeaders(SelectionSetNode? selectionSet)
    {
        if (selectionSet is null)
        {
            yield break;
        }

        foreach (var selection in selectionSet.Selections.OfType<FieldNode>())
        {
            if (selection.SelectionSet is null)
            {
                yield return selection.Alias?.Value ?? selection.Name.Value;
                continue;
            }

            foreach (var nested in CollectLeafHeaders(selection.SelectionSet))
            {
                yield return nested;
            }
        }
    }

    private static IReadOnlyList<(string FieldName, bool Descending)>? ParseOrderArgument(object? order) =>
        InputValueNormalizer.AsList(order)
            .SelectMany(item => InputValueNormalizer.AsDictionary(item)
                .Select(pair => (pair.Key, string.Equals(pair.Value?.ToString(), "DESC", StringComparison.Ordinal))))
            .ToArray();

    private static IReadOnlyList<FlatScalarFieldDefinition> GetApplicableFields(
        IReadOnlyList<FlatScalarFieldDefinition> scalarFields,
        AggregateOperator @operator) =>
        @operator switch
        {
            AggregateOperator.Sum or AggregateOperator.Avg or AggregateOperator.Stdev or AggregateOperator.Stdevp or AggregateOperator.Skew or AggregateOperator.Kurtosis =>
                scalarFields.Where(field => field.IsNumeric).ToArray(),
            AggregateOperator.StringAgg or AggregateOperator.StringAggDistinct =>
                scalarFields.Where(field => field.IsString).ToArray(),
            _ => scalarFields
        };

    private static string GetOperatorResultTypeName(CollectionFieldModel collectionField, bool isFlat, AggregateOperator @operator) =>
        $"{collectionField.TypePrefix}{(isFlat ? "Flat" : string.Empty)}{@operator}Result";

    private static string GetOperatorInputTypeName(CollectionFieldModel collectionField, bool isFlat, AggregateOperator @operator, string suffix) =>
        $"{collectionField.TypePrefix}{(isFlat ? "Flat" : string.Empty)}{@operator}{suffix}";

    private static string GetOperatorOutputType(AggregateOperator @operator, FlatScalarFieldDefinition field) =>
        @operator switch
        {
            AggregateOperator.CountDistinct => "Int",
            AggregateOperator.Sum or AggregateOperator.Avg or AggregateOperator.Stdev or AggregateOperator.Stdevp or AggregateOperator.Skew or AggregateOperator.Kurtosis => "Float",
            AggregateOperator.StringAgg or AggregateOperator.StringAggDistinct => "String",
            _ => GraphQlTypeReferenceHelper.GetOptionalScalarTypeSyntax(field.ClrType)
        };

    private readonly record struct FlatScalarFieldDefinition(string FieldName, Type ClrType, FlatScalarFieldKind Kind)
    {
        public bool IsNumeric => TypeInspection.IsNumeric(ClrType);

        public bool IsString => TypeInspection.IsStringLike(ClrType);
    }

    private static IObjectFieldDescriptor CreateSiblingField<THost>(
        IObjectTypeDescriptor<THost> descriptor,
        MemberInfo member,
        string fieldName) =>
        descriptor.Field(fieldName)
            .Resolve(async context => await ResolveCollectionSourceAsync<THost>(context, member));

    private static async ValueTask<object?> ResolveCollectionSourceAsync<THost>(
        IResolverContext context,
        MemberInfo member)
    {
        var source = context.Parent<THost>();

        return member switch
        {
            PropertyInfo property => property.GetValue(source),
            MethodInfo method => await InvokeMethodAsync(context, source, method),
            _ => throw new NotSupportedException($"Unsupported collection member {member}.")
        };
    }

    private static async ValueTask<object?> InvokeMethodAsync<THost>(
        IResolverContext context,
        THost source,
        MethodInfo method)
    {
        var arguments = method.GetParameters()
            .Select(parameter => ResolveMethodArgument(context, source, parameter))
            .ToArray();

        var value = method.Invoke(source, arguments);
        return await UnwrapTaskLikeAsync(value);
    }

    private static object? ResolveMethodArgument<THost>(
        IResolverContext context,
        THost source,
        ParameterInfo parameter)
    {
        if (parameter.ParameterType == typeof(CancellationToken))
        {
            return context.RequestAborted;
        }

        var attributes = parameter.GetCustomAttributes(inherit: true)
            .Select(attribute => attribute.GetType().Name)
            .ToArray();

        if (attributes.Contains("ParentAttribute", StringComparer.Ordinal))
        {
            return source;
        }

        if (attributes.Contains("ServiceAttribute", StringComparer.Ordinal))
        {
            return context.Services.GetRequiredService(parameter.ParameterType);
        }

        return parameter.HasDefaultValue ? parameter.DefaultValue : null;
    }

    private static async ValueTask<object?> UnwrapTaskLikeAsync(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case Task task:
                await task;
                return GetTaskResult(task);
        }

        var valueType = value.GetType();
        if (valueType == typeof(ValueTask))
        {
            await ((ValueTask)value);
            return null;
        }

        if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var task = (Task)valueType.GetMethod(nameof(ValueTask<object>.AsTask))!.Invoke(value, null)!;
            await task;
            return GetTaskResult(task);
        }

        return value;
    }

    private static object? GetTaskResult(Task task)
    {
        var taskType = task.GetType();
        return taskType.IsGenericType
            ? taskType.GetProperty(nameof(Task<object>.Result))!.GetValue(task)
            : null;
    }

    private enum FlatScalarFieldKind
    {
        Base,
        Generated
    }

    private enum ExportTargetKind
    {
        None,
        Flat,
        FlatAggregate,
        FlatGroup
    }
}
