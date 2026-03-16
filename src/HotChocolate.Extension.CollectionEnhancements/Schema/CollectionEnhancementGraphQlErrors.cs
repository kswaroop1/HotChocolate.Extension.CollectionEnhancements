using HotChocolate;
using HotChocolate.Extension.CollectionEnhancements.Metadata;

namespace HotChocolate.Extension.CollectionEnhancements.Schema;

internal static class CollectionEnhancementGraphQlErrors
{
    public const string UnexpectedAggregateParentContextCode = "CE_AGGREGATE_PARENT_CONTEXT_INVALID";
    public const string FlatExpandPathInvalidCode = "CE_FLAT_EXPAND_PATH_INVALID";
    public const string FlatFieldSelectionInvalidCode = "CE_FLAT_FIELD_SELECTION_INVALID";
    public const string ExportTargetInvalidCode = "CE_EXPORT_TARGET_INVALID";
    public const string ExportScopeInvalidCode = "CE_EXPORT_SCOPE_INVALID";
    public const string ExportRootSelectionInvalidCode = "CE_EXPORT_ROOT_SELECTION_INVALID";
    public const string ExportSeparatorInvalidCode = "CE_EXPORT_SEPARATOR_INVALID";
    public const string ExportDuplicateHeadersCode = "CE_EXPORT_DUPLICATE_HEADERS";

    public static GraphQLException UnexpectedAggregateParentContext() =>
        Create(
            UnexpectedAggregateParentContextCode,
            "Unexpected aggregate parent context.",
            static builder => builder
                .SetExtension("phase", "execution")
                .SetExtension("feature", "aggregate"));

    public static GraphQLException InvalidFlatFieldSelection(
        CollectionFieldModel collectionField,
        IReadOnlyList<string> invalidFields) =>
        Create(
            FlatFieldSelectionInvalidCode,
            $"The requested flat rowset does not include generated fields: {string.Join(", ", invalidFields)}.",
            builder => builder
                .SetExtension("phase", "validation")
                .SetExtension("feature", "flat")
                .SetExtension("collectionField", collectionField.GraphQlName)
                .SetExtension("invalidFields", invalidFields.ToArray()));

    public static GraphQLException InvalidFlatExpandPath(
        CollectionFieldModel collectionField,
        IReadOnlyList<string> invalidPaths) =>
        Create(
            FlatExpandPathInvalidCode,
            $"The requested flat expand paths are invalid: {string.Join(", ", invalidPaths)}.",
            builder => builder
                .SetExtension("phase", "validation")
                .SetExtension("feature", "flat")
                .SetExtension("collectionField", collectionField.GraphQlName)
                .SetExtension("invalidPaths", invalidPaths.ToArray()));

    public static GraphQLException InvalidExportTarget(string targetKind) =>
        Create(
            ExportTargetInvalidCode,
            "The @export directive is only supported on root flat or root flat-group fields.",
            builder => builder
                .SetExtension("phase", "validation")
                .SetExtension("feature", "export")
                .SetExtension("targetKind", targetKind));

    public static GraphQLException InvalidExportScope(string declaringTypeName) =>
        Create(
            ExportScopeInvalidCode,
            "The @export directive is only supported on root query fields.",
            builder => builder
                .SetExtension("phase", "validation")
                .SetExtension("feature", "export")
                .SetExtension("declaringType", declaringTypeName));

    public static GraphQLException InvalidExportRootSelectionCount(int rootSelectionCount) =>
        Create(
            ExportRootSelectionInvalidCode,
            "An export operation must select exactly one root field.",
            builder => builder
                .SetExtension("phase", "validation")
                .SetExtension("feature", "export")
                .SetExtension("rootSelectionCount", rootSelectionCount));

    public static GraphQLException InvalidExportSeparator(string separator) =>
        Create(
            ExportSeparatorInvalidCode,
            "The export separator must be a single character.",
            builder => builder
                .SetExtension("phase", "validation")
                .SetExtension("feature", "export")
                .SetExtension("separator", separator));

    public static GraphQLException DuplicateExportHeaders(IReadOnlyList<string> duplicateHeaders) =>
        Create(
            ExportDuplicateHeadersCode,
            $"The export selection produces duplicate headers: {string.Join(", ", duplicateHeaders)}.",
            builder => builder
                .SetExtension("phase", "validation")
                .SetExtension("feature", "export")
                .SetExtension("duplicateHeaders", duplicateHeaders.ToArray()));

    private static GraphQLException Create(
        string code,
        string message,
        Action<IErrorBuilder> configure)
    {
        var builder = ErrorBuilder.New()
            .SetCode(code)
            .SetMessage(message)
            .SetExtension("component", "CollectionEnhancements");
        configure(builder);
        return new GraphQLException(builder.Build());
    }
}
