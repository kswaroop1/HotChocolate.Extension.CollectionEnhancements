namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

public record Coupon(DateOnly ObservationDate, DateOnly PaymentDate, decimal? InterestRate);

public record Call(DateOnly ObservationDate, DateOnly CallDate, bool IsCalled);

public record Underlying(int Id, string Ric, string Currency, decimal StrikePrice);

public record SecurityDetails
{
    public int UnderlyingCount { get; init; }
    public int CouponCount { get; init; }
    public int CallCount { get; init; }
    public required Underlying[] Underlyings { get; init; }
    public required Coupon[] Coupons { get; init; }
    public required Call[] Calls { get; init; }
}

public record Security(int Id, string Isin, string Currency, decimal Price, decimal Notional, DateOnly StrikeDate, DateOnly ExpirationDate, SecurityDetails Details);
