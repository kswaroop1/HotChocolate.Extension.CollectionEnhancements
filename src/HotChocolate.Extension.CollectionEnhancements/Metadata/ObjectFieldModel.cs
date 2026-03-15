using System.Reflection;

namespace HotChocolate.Extension.CollectionEnhancements.Metadata;

internal abstract record ObjectFieldModel(
    MemberInfo Member,
    string GraphQlName,
    Type ClrType);

internal sealed record ScalarFieldModel(
    MemberInfo Member,
    string GraphQlName,
    Type ClrType)
    : ObjectFieldModel(Member, GraphQlName, ClrType);

internal sealed record ObjectReferenceFieldModel(
    MemberInfo Member,
    string GraphQlName,
    Type ClrType)
    : ObjectFieldModel(Member, GraphQlName, ClrType);

internal sealed record CollectionFieldModel(
    MemberInfo Member,
    string GraphQlName,
    Type ClrType,
    Type ElementType,
    string HostTypeName,
    string ElementTypeName)
    : ObjectFieldModel(Member, GraphQlName, ClrType)
{
    public string TypePrefix => $"{HostTypeName}{GraphQlNaming.ToPascalCase(GraphQlName)}";

    public string AggregateFieldName => $"{GraphQlName}Aggregate";

    public string GroupFieldName => $"{GraphQlName}Group";

    public string FlatFieldName => $"{GraphQlName}Flat";

    public string FlatAggregateFieldName => $"{GraphQlName}FlatAggregate";

    public string FlatGroupFieldName => $"{GraphQlName}FlatGroup";

    public string FilterInputName => $"{ElementTypeName}FilterInput";

    public string SortInputName => $"{ElementTypeName}SortInput";

    public string AggregateCriteriaInputName => $"{TypePrefix}AggregateCriteriaInput";

    public string AggregateResultName => $"{TypePrefix}AggregateResult";

    public string AggregateHavingInputName => $"{TypePrefix}AggregateHavingInput";

    public string GroupCriteriaInputName => $"{TypePrefix}GroupCriteriaInput";

    public string GroupRowName => $"{TypePrefix}GroupRow";

    public string GroupKeyName => $"{TypePrefix}GroupKey";

    public string GroupByEnumName => $"{TypePrefix}GroupByInput";

    public string GroupOrderInputName => $"{TypePrefix}GroupOrderInput";

    public string GroupHavingInputName => $"{TypePrefix}GroupHavingInput";

    public string FlatRowName => $"{TypePrefix}FlatRow";

    public string FlatRowFilterInputName => $"{TypePrefix}FlatRowFilterInput";

    public string FlatRowSortInputName => $"{TypePrefix}FlatRowSortInput";

    public string FlatAggregateResultName => $"{TypePrefix}FlatAggregateResult";

    public string FlatAggregateHavingInputName => $"{TypePrefix}FlatAggregateHavingInput";

    public string FlatGroupRowName => $"{TypePrefix}FlatGroupRow";

    public string FlatGroupKeyName => $"{TypePrefix}FlatGroupKey";

    public string FlatGroupByEnumName => $"{TypePrefix}FlatGroupByInput";

    public string FlatGroupOrderInputName => $"{TypePrefix}FlatGroupOrderInput";

    public string FlatGroupHavingInputName => $"{TypePrefix}FlatGroupHavingInput";
}
