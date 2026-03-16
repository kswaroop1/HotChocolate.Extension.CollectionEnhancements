namespace HotChocolate.Extension.CollectionEnhancements;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class CollectionEnhancementModelAttribute : Attribute
{
    public bool IsQueryRoot { get; init; }
}
