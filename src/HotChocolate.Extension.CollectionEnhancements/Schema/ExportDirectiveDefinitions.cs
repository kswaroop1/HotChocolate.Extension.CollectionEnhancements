using HotChocolate.Types;

namespace HotChocolate.Extension.CollectionEnhancements.Schema;

internal enum ExportFormat
{
    Csv
}

internal sealed class ExportFormatType : EnumType<ExportFormat>
{
    protected override void Configure(IEnumTypeDescriptor<ExportFormat> descriptor)
    {
        descriptor.Name("ExportFormat");
        descriptor.BindValuesExplicitly();
        descriptor.Value(ExportFormat.Csv).Name("CSV");
    }
}

internal sealed class ExportDirectiveType : DirectiveType
{
    protected override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        descriptor.Name("export");
        descriptor.Location(DirectiveLocation.Field);
        descriptor.Argument("format").Type<NonNullType<ExportFormatType>>().DefaultValue(ExportFormat.Csv);
        descriptor.Argument("separator").Type<StringType>().DefaultValue(",");
        descriptor.Argument("includeHeader").Type<BooleanType>().DefaultValue(true);
        descriptor.Argument("fileName").Type<StringType>();
    }
}
