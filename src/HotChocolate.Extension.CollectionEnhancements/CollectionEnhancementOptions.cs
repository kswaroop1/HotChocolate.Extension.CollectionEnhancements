namespace HotChocolate.Extension.CollectionEnhancements;

public sealed class CollectionEnhancementOptions
{
    public StatisticalMomentsExecutionMode StatisticalMomentsExecutionMode { get; set; } =
        StatisticalMomentsExecutionMode.Fast;
}

public enum StatisticalMomentsExecutionMode
{
    Fast,
    Stable
}
