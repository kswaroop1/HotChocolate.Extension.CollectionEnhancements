namespace HotChocolate.Extension.CollectionEnhancements.Metadata;

internal sealed class ObjectTypeModel(
    Type clrType,
    string requestedGraphQlTypeName,
    bool isQueryRoot)
{
    public Type ClrType { get; } = clrType;

    public string RequestedGraphQlTypeName { get; } = requestedGraphQlTypeName;

    public string GraphQlTypeName { get; internal set; } = requestedGraphQlTypeName;

    public bool IsQueryRoot { get; } = isQueryRoot;

    public List<ScalarFieldModel> ScalarFields { get; } = [];

    public List<ObjectReferenceFieldModel> ObjectFields { get; } = [];

    public List<CollectionFieldModel> CollectionFields { get; } = [];

    public string FilterInputName => $"{GraphQlTypeName}FilterInput";

    public string SortInputName => $"{GraphQlTypeName}SortInput";

    public ScalarFieldModel? FindScalar(string fieldName) =>
        ScalarFields.FirstOrDefault(field => field.GraphQlName == fieldName);

    public ObjectReferenceFieldModel? FindObject(string fieldName) =>
        ObjectFields.FirstOrDefault(field => field.GraphQlName == fieldName);

    public CollectionFieldModel? FindCollection(string fieldName) =>
        CollectionFields.FirstOrDefault(field => field.GraphQlName == fieldName);
}
