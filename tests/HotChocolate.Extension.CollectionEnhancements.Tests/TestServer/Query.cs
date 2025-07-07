using HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestServer;

public sealed class Query
{
    [UseFiltering]
    [UseSorting]
    public IQueryable<Security> GetSecurities([Service] ISecurityService securityService)
        => securityService.GetSecurities();

    [UseFiltering]
    [UseSorting]
    public IQueryable<Customer> GetCustomers([Service] ICustomerService customerService)
        => customerService.GetCustomers();

    [UseFiltering]
    [UseSorting]
    public IQueryable<Person> GetPeople([Service] IPersonService personService)
        => personService.GetPeople();
}
