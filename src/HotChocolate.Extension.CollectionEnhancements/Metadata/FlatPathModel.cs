using System.Reflection;

namespace HotChocolate.Extension.CollectionEnhancements.Metadata;

internal sealed class FlatPathModel(
    string path,
    string prefix,
    CollectionFieldModel rootCollection,
    IReadOnlyList<MemberInfo> segments,
    Type terminalElementType,
    string terminalTypeName)
{
    public string Path { get; } = path;

    public string Prefix { get; } = prefix;

    public CollectionFieldModel RootCollection { get; } = rootCollection;

    public IReadOnlyList<MemberInfo> Segments { get; } = segments;

    public Type TerminalElementType { get; } = terminalElementType;

    public string TerminalTypeName { get; } = terminalTypeName;
}
