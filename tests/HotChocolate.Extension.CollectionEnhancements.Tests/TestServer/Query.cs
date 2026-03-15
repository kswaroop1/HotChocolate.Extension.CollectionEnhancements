using HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;

public sealed class Query
{
    public IQueryable<Security> GetSecurities([Service] ISecurityService securityService)
        => securityService.GetSecurities();

    public IQueryable<Customer> GetCustomers([Service] ICustomerService customerService)
        => customerService.GetCustomers();

    public IQueryable<Person> GetPeople([Service] IPersonService personService)
        => personService.GetPeople();
}
