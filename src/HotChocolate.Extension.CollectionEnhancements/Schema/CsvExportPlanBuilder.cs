using HotChocolate.Language;

namespace HotChocolate.Extension.CollectionEnhancements.Schema;

internal static class CsvExportPlanBuilder
{
    public static CsvExportPlan? TryCreate(DocumentNode? document, string? operationName)
    {
        var operation = GetOperation(document, operationName);
        if (operation is null)
        {
            return null;
        }

        var rootFields = operation.SelectionSet.Selections
            .OfType<FieldNode>()
            .ToArray();

        if (rootFields.Length != 1)
        {
            return null;
        }

        var rootField = rootFields[0];
        var exportDirective = rootField.Directives
            .FirstOrDefault(candidate => candidate.Name.Value == "export");

        if (exportDirective is null)
        {
            return null;
        }

        var format = ResolverArgumentReader.GetDirectiveArgument(exportDirective, "format")?.ToString() ?? "CSV";
        if (!string.Equals(format, "CSV", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var separator = ResolverArgumentReader.GetDirectiveArgument(exportDirective, "separator")?.ToString() ?? ",";
        if (separator.Length != 1)
        {
            return null;
        }

        var includeHeader = ResolverArgumentReader.GetDirectiveArgument(exportDirective, "includeHeader") switch
        {
            bool value => value,
            _ => true
        };

        var fileName = ResolverArgumentReader.GetDirectiveArgument(exportDirective, "fileName")?.ToString();
        var columns = new List<CsvExportColumn>();
        CollectColumns(rootField.SelectionSet, [], columns);

        return columns.Count == 0
            ? null
            : new CsvExportPlan(
                RootResponseName: rootField.Alias?.Value ?? rootField.Name.Value,
                Separator: separator,
                IncludeHeader: includeHeader,
                FileName: fileName,
                Columns: columns);
    }

    private static OperationDefinitionNode? GetOperation(DocumentNode? document, string? operationName)
    {
        if (document is null)
        {
            return null;
        }

        var operations = document.Definitions
            .OfType<OperationDefinitionNode>()
            .ToArray();

        if (!string.IsNullOrWhiteSpace(operationName))
        {
            return operations.FirstOrDefault(candidate =>
                string.Equals(candidate.Name?.Value, operationName, StringComparison.Ordinal));
        }

        return operations switch
        {
            [] => null,
            [var operation] => operation,
            _ => operations.FirstOrDefault(candidate => candidate.Operation == OperationType.Query)
        };
    }

    private static void CollectColumns(
        SelectionSetNode? selectionSet,
        IReadOnlyList<string> prefix,
        ICollection<CsvExportColumn> columns)
    {
        if (selectionSet is null)
        {
            return;
        }

        foreach (var field in selectionSet.Selections.OfType<FieldNode>())
        {
            var responseName = field.Alias?.Value ?? field.Name.Value;
            var path = prefix.Concat([responseName]).ToArray();

            if (field.SelectionSet is null)
            {
                columns.Add(new CsvExportColumn(responseName, path));
                continue;
            }

            CollectColumns(field.SelectionSet, path, columns);
        }
    }
}
