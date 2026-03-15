namespace HotChocolate.Extension.CollectionEnhancements.Execution;

internal static class ComparisonHelper
{
    public static bool EqualsValue(object? left, object? right) =>
        left switch
        {
            null => right is null,
            _ when TryConvertToDecimal(left, out var leftDecimal) && TryConvertToDecimal(right, out var rightDecimal) => leftDecimal == rightDecimal,
            DateOnly leftDate when TryConvertToDateOnly(right, out var rightDate) => leftDate == rightDate,
            DateTime leftDateTime when TryConvertToDateTime(right, out var rightDateTime) => leftDateTime == rightDateTime,
            Enum leftEnum when right is Enum rightEnum => leftEnum.ToString() == rightEnum.ToString(),
            Enum leftEnum when right is string rightText => string.Equals(leftEnum.ToString(), rightText, StringComparison.Ordinal),
            _ => Equals(left, right)
        };

    public static int Compare(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (TryConvertToDecimal(left, out var leftDecimal) && TryConvertToDecimal(right, out var rightDecimal))
        {
            return leftDecimal.CompareTo(rightDecimal);
        }

        return left switch
        {
            DateOnly leftDate when TryConvertToDateOnly(right, out var rightDate) => leftDate.CompareTo(rightDate),
            DateTime leftDateTime when TryConvertToDateTime(right, out var rightDateTime) => leftDateTime.CompareTo(rightDateTime),
            string leftString when right is string rightString => string.CompareOrdinal(leftString, rightString),
            Enum leftEnum when right is Enum rightEnum => string.CompareOrdinal(leftEnum.ToString(), rightEnum.ToString()),
            IComparable comparable => comparable.CompareTo(right),
            _ => string.CompareOrdinal(left.ToString(), right.ToString())
        };
    }

    public static bool TryConvertToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case null:
                result = default;
                return false;
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case float floatValue:
                result = (decimal)floatValue;
                return true;
            case double doubleValue:
                result = (decimal)doubleValue;
                return true;
            case string text when decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = default;
                return false;
        }
    }

    public static bool TryConvertToDouble(object? value, out double result)
    {
        switch (value)
        {
            case null:
                result = default;
                return false;
            case decimal decimalValue:
                result = (double)decimalValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case double doubleValue:
                result = doubleValue;
                return true;
            case string text when double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryConvertToDateOnly(object? value, out DateOnly result)
    {
        switch (value)
        {
            case DateOnly dateOnly:
                result = dateOnly;
                return true;
            case DateTime dateTime:
                result = DateOnly.FromDateTime(dateTime);
                return true;
            case string text when DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate):
                result = parsedDate;
                return true;
            case string text when DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDateTime):
                result = DateOnly.FromDateTime(parsedDateTime);
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryConvertToDateTime(object? value, out DateTime result)
    {
        switch (value)
        {
            case DateTime dateTime:
                result = dateTime;
                return true;
            case DateOnly dateOnly:
                result = dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                return true;
            case string text when DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate):
                result = parsedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                return true;
            case string text when DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed):
                result = parsed;
                return true;
            default:
                result = default;
                return false;
        }
    }
}
