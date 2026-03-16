namespace HotChocolate.Extension.CollectionEnhancements.Generated;

public enum CollectionEnhancementGeneratedMemberKind
{
    Property,
    Method
}

public sealed record CollectionEnhancementGeneratedScalarField(
    string MemberName,
    CollectionEnhancementGeneratedMemberKind MemberKind,
    string GraphQlName,
    Type ClrType);

public sealed record CollectionEnhancementGeneratedObjectField(
    string MemberName,
    CollectionEnhancementGeneratedMemberKind MemberKind,
    string GraphQlName,
    Type ClrType);

public sealed record CollectionEnhancementGeneratedFlatPath(
    string Path,
    string Prefix,
    Type TerminalElementType,
    string TerminalTypeName,
    IReadOnlyList<string> SegmentMemberNames);

public sealed record CollectionEnhancementGeneratedCollectionField(
    string MemberName,
    CollectionEnhancementGeneratedMemberKind MemberKind,
    string GraphQlName,
    Type ClrType,
    Type ElementType,
    string HostTypeName,
    string ElementTypeName,
    Type? FlatRowClrType,
    IReadOnlyList<CollectionEnhancementGeneratedFlatPath> FlatPaths);

public sealed record CollectionEnhancementGeneratedObjectType(
    Type ClrType,
    string GraphQlTypeName,
    bool IsQueryRoot,
    IReadOnlyList<CollectionEnhancementGeneratedScalarField> ScalarFields,
    IReadOnlyList<CollectionEnhancementGeneratedObjectField> ObjectFields,
    IReadOnlyList<CollectionEnhancementGeneratedCollectionField> CollectionFields);

public interface ICollectionEnhancementGeneratedModelProvider
{
    IReadOnlyList<CollectionEnhancementGeneratedObjectType> GetObjectTypes();
}
