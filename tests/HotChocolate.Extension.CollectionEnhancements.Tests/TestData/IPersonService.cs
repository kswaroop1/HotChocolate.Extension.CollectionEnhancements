namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

public interface IPersonService
{
    IQueryable<Person> GetPeople();
}
