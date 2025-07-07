namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

public sealed class TestCustomerService : ICustomerService
{
    public IQueryable<Customer> GetCustomers() => ExampleData.Customers.AsQueryable();
}
