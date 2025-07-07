namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

public sealed class TestPersonService : IPersonService
{
    public IQueryable<Person> GetPeople() => ExampleData.People.AsQueryable();
}
