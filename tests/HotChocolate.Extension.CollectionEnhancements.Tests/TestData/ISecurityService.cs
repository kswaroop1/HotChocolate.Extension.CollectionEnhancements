namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

public interface ISecurityService
{
    IQueryable<Security> GetSecurities();
}