namespace HotChocolate.Extension.CollectionEnhancements.Schema;

internal sealed record CsvExportPlan(
    string RootResponseName,
    string Separator,
    bool IncludeHeader,
    string? FileName,
    IReadOnlyList<CsvExportColumn> Columns);

internal sealed record CsvExportColumn(
    string Header,
    IReadOnlyList<string> ResponsePath);
