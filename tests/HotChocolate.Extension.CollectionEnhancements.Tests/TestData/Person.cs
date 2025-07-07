namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

public record Comment(int Id, string Body, string Author);

public record Post(int Id, string Title, Comment[] Comments);

public record Person(int Id, string Name, Post[] Posts, Order[] Orders);
