namespace HotChocolate.Extension.CollectionEnhancements.Tests.Specs;

public enum SpecCaseExpectation
{
    Success,
    ValidationFailure
}

public enum SpecCaseStatus
{
    PendingCollectionEnhancements,
    Enabled
}

public sealed record ExecutableSpecCase(
    string Id,
    string Description,
    string Query,
    SpecCaseExpectation Expectation,
    SpecCaseStatus Status,
    string Notes)
{
    public override string ToString() => $"{Id}: {Description}";
}
