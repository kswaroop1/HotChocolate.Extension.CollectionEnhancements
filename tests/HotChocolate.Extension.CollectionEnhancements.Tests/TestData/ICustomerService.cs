namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

public interface ICustomerService
{
    IQueryable<Customer> GetCustomers();
}
