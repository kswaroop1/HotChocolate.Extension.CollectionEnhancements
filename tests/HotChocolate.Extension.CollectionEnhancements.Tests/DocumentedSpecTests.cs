using HotChocolate.Extension.CollectionEnhancements.Tests.Specs;
using HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;

namespace HotChocolate.Extension.CollectionEnhancements.Tests;

[Trait("Suite", "ExecutableSpec")]
public sealed class DocumentedSpecTests
{
    [Theory(Skip = "Pending collection enhancement implementation. The catalog remains executable-spec scaffolding for future work.")]
    [MemberData(nameof(DocumentedSpecCatalog.All), MemberType = typeof(DocumentedSpecCatalog))]
    public async Task Canonical_Documented_Examples_Should_Remain_Bound_To_Named_Spec_Cases(
        ExecutableSpecCase specCase)
    {
        var executor = await TestServerFactory.CreateTestExecutorAsync();
        var result = await executor.ExecuteQueryResultAsync(specCase.Query);

        switch (specCase.Expectation)
        {
            case SpecCaseExpectation.Success:
                using (result.AssertSuccessfulJson())
                {
                }
                break;
            case SpecCaseExpectation.ValidationFailure:
                result.AssertHasErrors();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(specCase.Expectation), specCase.Expectation, null);
        }
    }
}
