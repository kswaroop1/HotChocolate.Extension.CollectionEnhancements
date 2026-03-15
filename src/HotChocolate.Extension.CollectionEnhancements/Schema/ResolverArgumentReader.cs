using HotChocolate.Language;
using HotChocolate.Resolvers;

namespace HotChocolate.Extension.CollectionEnhancements.Schema;

internal static class ResolverArgumentReader
{
    public static object? GetOptionalArgument(IResolverContext context, string name)
    {
        var value = context.Selection.SyntaxNode.Arguments.FirstOrDefault(argument => argument.Name.Value == name)?.Value;
        if (value is null or NullValueNode)
        {
            return null;
        }

        return value is VariableNode
            ? Execution.InputValueNormalizer.Normalize(context.ArgumentValue<object?>(name))
            : ParseValue(value);
    }

    public static int? GetNullableInt(IResolverContext context, string name) =>
        GetOptionalArgument(context, name) switch
        {
            null => null,
            int value => value,
            long value => (int)value,
            double value => (int)value,
            _ => int.Parse(GetOptionalArgument(context, name)!.ToString()!, System.Globalization.CultureInfo.InvariantCulture)
        };

    public static string? GetRequiredString(IResolverContext context, string name) =>
        GetOptionalArgument(context, name)?.ToString();

    public static IReadOnlyList<string> GetStringList(IResolverContext context, string name) =>
        Execution.InputValueNormalizer.AsList(GetOptionalArgument(context, name))
            .Select(item => item?.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();

    public static object? GetDirectiveArgument(DirectiveNode directive, string name)
    {
        var value = directive.Arguments.FirstOrDefault(argument => argument.Name.Value == name)?.Value;
        if (value is null or NullValueNode)
        {
            return null;
        }

        return ParseValue(value);
    }

    private static object? ParseValue(IValueNode value) =>
        value switch
        {
            NullValueNode => null,
            StringValueNode stringValue => stringValue.Value,
            EnumValueNode enumValue => enumValue.Value,
            BooleanValueNode booleanValue => booleanValue.Value,
            IntValueNode intValue when long.TryParse(intValue.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedLong) => parsedLong,
            FloatValueNode floatValue when double.TryParse(floatValue.Value, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble) => parsedDouble,
            ListValueNode listValue => listValue.Items.Select(ParseValue).ToArray(),
            ObjectValueNode objectValue => objectValue.Fields.ToDictionary(
                field => field.Name.Value,
                field => ParseValue(field.Value),
                StringComparer.Ordinal),
            VariableNode => null,
            _ => value.ToString()
        };
}
