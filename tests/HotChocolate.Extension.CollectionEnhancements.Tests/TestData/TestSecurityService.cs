namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

public sealed class TestSecurityService : ISecurityService
{
    public IQueryable<Security> GetSecurities() => ExampleData.Securities.AsQueryable();
}
