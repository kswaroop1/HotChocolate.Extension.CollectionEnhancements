namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

public record Tag(int Id, string TagName, string Category);

public record OrderItem(int Id, string ProductName, int Quantity, Tag[] Tags);

public enum OrderStatus
{
    Pending,
    Active,
    Cancelled,
    Completed
}

public record Order(
    int Id,
    DateTime CreatedAt,
    decimal Total,
    OrderStatus Status,
    string Reference,
    OrderItem[] Items);

public record Customer(int Id, string Name, Order[] Orders);
