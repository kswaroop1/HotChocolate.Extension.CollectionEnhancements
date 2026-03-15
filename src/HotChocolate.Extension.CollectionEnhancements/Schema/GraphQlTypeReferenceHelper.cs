using HotChocolate.Extension.CollectionEnhancements.Metadata;

namespace HotChocolate.Extension.CollectionEnhancements.Schema;

internal static class GraphQlTypeReferenceHelper
{
    public static string Parse(string typeSyntax) => typeSyntax;

    public static string GetScalarTypeName(Type clrType)
    {
        var actualType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (actualType.IsEnum)
        {
            return GraphQlNaming.GetTypeName(actualType);
        }

        return actualType switch
        {
            var t when t == typeof(bool) => "Boolean",
            var t when t == typeof(byte) || t == typeof(short) || t == typeof(int) || t == typeof(long) => "Int",
            var t when t == typeof(float) || t == typeof(double) => "Float",
            var t when t == typeof(decimal) => "Decimal",
            var t when t == typeof(string) => "String",
            var t when t == typeof(DateOnly) => "String",
            var t when t == typeof(DateTime) => "String",
            var t when t == typeof(Guid) => "String",
            _ => "String"
        };
    }

    public static string GetRequiredScalarTypeSyntax(Type clrType) => GetScalarTypeName(clrType) + "!";

    public static string GetOptionalScalarTypeSyntax(Type clrType) => GetScalarTypeName(clrType);

    public static string GetOperationFilterTypeName(Type clrType)
    {
        var actualType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (actualType.IsEnum)
        {
            return $"Ce{GraphQlNaming.GetTypeName(actualType)}OperationFilterInput";
        }

        if (TypeInspection.IsNumeric(actualType))
        {
            return "CeFloatOperationFilterInput";
        }

        return actualType switch
        {
            var t when t == typeof(bool) => "CeBooleanOperationFilterInput",
            _ => "CeStringOperationFilterInput"
        };
    }
}
