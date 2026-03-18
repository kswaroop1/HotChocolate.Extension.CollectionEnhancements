using HotChocolate;
using HotChocolate.Extension.CollectionEnhancements;

namespace HotChocolate.Extension.CollectionEnhancements.Tests.Collision.Region.Alpha
{
    public sealed record Order(int Id, string Code);
}

namespace HotChocolate.Extension.CollectionEnhancements.Tests.Collision
{
    public sealed record RegionAlphaOrder(int Id);
}

namespace HotChocolate.Extension.CollectionEnhancements.Tests.Collision.Sales.Alpha
{
    public sealed record Order(int Id, decimal Total);
}

namespace HotChocolate.Extension.CollectionEnhancements.Tests.Collision.Billing.Region.Alpha
{
    public sealed record Order(int Id);
}

namespace HotChocolate.Extension.CollectionEnhancements.Tests.Collision.Sales.Region.Alpha
{
    public sealed record Order(int Id);
}

namespace HotChocolate.Extension.CollectionEnhancements.Tests.Collision.Model
{
    public sealed record Query(int Id);
}

namespace HotChocolate.Extension.CollectionEnhancements.Tests
{
    public sealed class DuplicateNameQuery
    {
        public Collision.Region.Alpha.Order[] GetRegionalOrders() =>
        [
            new(1, "R1")
        ];

        public Collision.Sales.Alpha.Order[] GetSalesOrders() =>
        [
            new(2, 12.5m)
        ];
    }

    public sealed class PrimaryQueryRoot
    {
        public Collision.Region.Alpha.Order[] GetRegionalOrders() =>
        [
            new(1, "R1")
        ];
    }

    [CollectionEnhancementModel(IsQueryRoot = true)]
    [ExtendObjectType("Query")]
    public sealed class SecondaryQueryRoot
    {
        public Collision.Sales.Alpha.Order[] GetSalesOrders() =>
        [
            new(2, 12.5m)
        ];
    }

    public sealed class AssignedCandidateCollisionQuery
    {
        public Collision.RegionAlphaOrder[] GetRegionAlphaOrders() =>
        [
            new(1)
        ];

        public Collision.Billing.Region.Alpha.Order[] GetBillingOrders() =>
        [
            new(2)
        ];

        public Collision.Sales.Region.Alpha.Order[] GetSalesOrders() =>
        [
            new(3)
        ];
    }

    public sealed class ReservedQueryNameCollisionRoot
    {
        public Collision.Model.Query[] GetQueries() =>
        [
            new(4)
        ];
    }
}
