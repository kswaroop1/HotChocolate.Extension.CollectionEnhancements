namespace HotChocolate.Extension.CollectionEnhancements.Tests.TestData;

public static class ExampleData
{
    public static IReadOnlyList<Security> Securities { get; } = CreateSecurities();

    public static IReadOnlyList<Customer> Customers { get; } = CreateCustomers();

    public static IReadOnlyList<Person> People { get; } = CreatePeople();

    private static IReadOnlyList<Security> CreateSecurities() =>
    [
        new(
            Id: 1,
            Isin: "US123456789",
            Currency: "USD",
            Price: 100.50m,
            Notional: 1_000_000m,
            StrikeDate: new DateOnly(2023, 1, 15),
            ExpirationDate: new DateOnly(2025, 1, 15),
            Details: new SecurityDetails
            {
                Underlyings =
                [
                    new(1, "AAPL", "USD", 150.00m),
                    new(2, "MSFT", "USD", 300.00m),
                    new(3, "GOOGL", "USD", 2_500.00m)
                ],
                Coupons =
                [
                    new(new DateOnly(2023, 3, 15), new DateOnly(2023, 3, 20), 0.05m),
                    new(new DateOnly(2023, 6, 15), new DateOnly(2023, 6, 20), 0.045m),
                    new(new DateOnly(2023, 9, 15), new DateOnly(2023, 9, 20), 0.055m),
                    new(new DateOnly(2023, 12, 15), new DateOnly(2023, 12, 20), 0.06m),
                    new(new DateOnly(2024, 3, 15), new DateOnly(2024, 3, 20), 0.04m),
                    new(new DateOnly(2024, 6, 15), new DateOnly(2024, 6, 20), 0.035m)
                ],
                Calls =
                [
                    new(new DateOnly(2023, 6, 1), new DateOnly(2023, 6, 15), false),
                    new(new DateOnly(2023, 12, 1), new DateOnly(2023, 12, 15), true),
                    new(new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 15), false)
                ],
                UnderlyingCount = 3,
                CouponCount = 6,
                CallCount = 3
            }),
        new(
            Id: 2,
            Isin: "US987654321",
            Currency: "EUR",
            Price: 95.25m,
            Notional: 500_000m,
            StrikeDate: new DateOnly(2023, 2, 10),
            ExpirationDate: new DateOnly(2024, 12, 10),
            Details: new SecurityDetails
            {
                Underlyings =
                [
                    new(4, "SAP", "EUR", 120.00m),
                    new(5, "ASML", "EUR", 600.00m)
                ],
                Coupons =
                [
                    new(new DateOnly(2023, 5, 10), new DateOnly(2023, 5, 15), 0.03m),
                    new(new DateOnly(2023, 8, 10), new DateOnly(2023, 8, 15), 0.025m),
                    new(new DateOnly(2023, 11, 10), new DateOnly(2023, 11, 15), 0.04m)
                ],
                Calls =
                [
                    new(new DateOnly(2023, 8, 1), new DateOnly(2023, 8, 10), true),
                    new(new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 10), false)
                ],
                UnderlyingCount = 2,
                CouponCount = 3,
                CallCount = 2
            }),
        new(
            Id: 3,
            Isin: "JP555444333",
            Currency: "JPY",
            Price: 10_250m,
            Notional: 100_000m,
            StrikeDate: new DateOnly(2023, 4, 1),
            ExpirationDate: new DateOnly(2024, 6, 1),
            Details: new SecurityDetails
            {
                Underlyings =
                [
                    new(6, "7203.T", "JPY", 2_800m),
                    new(7, "6758.T", "JPY", 8_500m),
                    new(8, "9984.T", "JPY", 15_000m),
                    new(9, "8306.T", "JPY", 4_200m)
                ],
                Coupons =
                [
                    new(new DateOnly(2023, 7, 1), new DateOnly(2023, 7, 5), 0.02m),
                    new(new DateOnly(2023, 10, 1), new DateOnly(2023, 10, 5), 0.015m),
                    new(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5), 0.025m),
                    new(new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 5), null)
                ],
                Calls =
                [
                    new(new DateOnly(2023, 10, 1), new DateOnly(2023, 10, 5), false),
                    new(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5), false),
                    new(new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 5), true)
                ],
                UnderlyingCount = 4,
                CouponCount = 4,
                CallCount = 3
            }),
        new(
            Id: 4,
            Isin: "GB112233445",
            Currency: "GBP",
            Price: 88.75m,
            Notional: 250_000m,
            StrikeDate: new DateOnly(2023, 5, 20),
            ExpirationDate: new DateOnly(2024, 11, 20),
            Details: new SecurityDetails
            {
                Underlyings = [new(10, "BARC.L", "GBP", 180.00m)],
                Coupons =
                [
                    new(new DateOnly(2023, 8, 20), new DateOnly(2023, 8, 25), 0.028m),
                    new(new DateOnly(2024, 2, 20), new DateOnly(2024, 2, 25), 0.031m)
                ],
                Calls = [new(new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 15), false)],
                UnderlyingCount = 1,
                CouponCount = 2,
                CallCount = 1
            })
    ];

    private static IReadOnlyList<Customer> CreateCustomers()
    {
        var vip = new Tag(1, "VIP", "Segment");
        var fixedIncome = new Tag(2, "FixedIncome", "Asset");
        var campaign = new Tag(3, "CampaignQ1", "Promotion");
        var urgent = new Tag(4, "Urgent", "Workflow");

        return
        [
            new(
                Id: 1,
                Name: "Alice Capital",
                Orders:
                [
                    new(
                        Id: 1001,
                        CreatedAt: new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc),
                        Total: 650m,
                        Status: OrderStatus.Active,
                        Reference: "ORD-ALPHA",
                        Items:
                        [
                            new(1, "Bond Ladder", 2, [vip, fixedIncome]),
                            new(2, "Structured Note", 1, [campaign])
                        ]),
                    new(
                        Id: 1002,
                        CreatedAt: new DateTime(2026, 3, 5, 13, 30, 0, DateTimeKind.Utc),
                        Total: 450m,
                        Status: OrderStatus.Active,
                        Reference: "ORD-BETA",
                        Items:
                        [
                            new(3, "Income Basket", 1, [fixedIncome]),
                            new(4, "Autocall Overlay", 3, [vip, urgent])
                        ]),
                    new(
                        Id: 1003,
                        CreatedAt: new DateTime(2026, 2, 18, 16, 15, 0, DateTimeKind.Utc),
                        Total: 200m,
                        Status: OrderStatus.Active,
                        Reference: "ORD-ALPHA",
                        Items:
                        [
                            new(5, "Callable Overlay", 1, [urgent])
                        ]),
                    new(
                        Id: 1004,
                        CreatedAt: new DateTime(2026, 1, 20, 8, 45, 0, DateTimeKind.Utc),
                        Total: 300m,
                        Status: OrderStatus.Cancelled,
                        Reference: "ORD-CANCEL",
                        Items:
                        [
                            new(6, "Legacy Note", 1, [campaign])
                        ])
                ]),
            new(
                Id: 2,
                Name: "Bob Treasury",
                Orders:
                [
                    new(
                        Id: 2001,
                        CreatedAt: new DateTime(2026, 3, 1, 11, 0, 0, DateTimeKind.Utc),
                        Total: 400m,
                        Status: OrderStatus.Pending,
                        Reference: "ORD-GAMMA",
                        Items:
                        [
                            new(7, "FX Basket", 2, [campaign])
                        ]),
                    new(
                        Id: 2002,
                        CreatedAt: new DateTime(2026, 2, 20, 10, 30, 0, DateTimeKind.Utc),
                        Total: 550m,
                        Status: OrderStatus.Active,
                        Reference: "ORD-DELTA",
                        Items:
                        [
                            new(8, "Rates Hedge", 1, [fixedIncome, urgent])
                        ])
                ]),
            new(
                Id: 3,
                Name: "Cara Holdings",
                Orders:
                [
                    new(
                        Id: 3001,
                        CreatedAt: new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc),
                        Total: 1_500m,
                        Status: OrderStatus.Completed,
                        Reference: "ORD-OMEGA",
                        Items:
                        [
                            new(9, "Long-Dated Callable", 1, [vip])
                        ])
                ])
        ];
    }

    private static IReadOnlyList<Person> CreatePeople()
    {
        var priority = new Tag(10, "Priority", "Workflow");
        var retail = new Tag(11, "Retail", "Channel");
        var structured = new Tag(12, "Structured", "Asset");
        var seasonal = new Tag(13, "Seasonal", "Promotion");

        return
        [
            new(
                Id: 1,
                Name: "Eve Trader",
                Posts:
                [
                    new(
                        1,
                        "Macro Ideas",
                        [
                            new(1, "Watch front-end rates", "Ana"),
                            new(2, "Credit spreads are tightening", "Ben")
                        ]),
                    new(
                        2,
                        "Desk Notes",
                        [
                            new(3, "Coupon rolls next week", "Cara")
                        ])
                ],
                Orders:
                [
                    new(
                        Id: 4001,
                        CreatedAt: new DateTime(2026, 3, 14, 8, 0, 0, DateTimeKind.Utc),
                        Total: 900m,
                        Status: OrderStatus.Active,
                        Reference: "P-ALPHA",
                        Items:
                        [
                            new(10, "Yield Basket", 2, [priority, structured]),
                            new(11, "Callable Sleeve", 1, [seasonal])
                        ]),
                    new(
                        Id: 4002,
                        CreatedAt: new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc),
                        Total: 120m,
                        Status: OrderStatus.Pending,
                        Reference: "P-BETA",
                        Items:
                        [
                            new(12, "Retail Note", 4, [retail])
                        ]),
                    new(
                        Id: 4003,
                        CreatedAt: new DateTime(2026, 2, 28, 15, 0, 0, DateTimeKind.Utc),
                        Total: 250m,
                        Status: OrderStatus.Completed,
                        Reference: "P-GAMMA",
                        Items:
                        [
                            new(13, "Step-Up Coupon", 1, [structured, priority])
                        ]),
                    new(
                        Id: 4004,
                        CreatedAt: new DateTime(2026, 2, 5, 9, 15, 0, DateTimeKind.Utc),
                        Total: 80m,
                        Status: OrderStatus.Cancelled,
                        Reference: "P-DELTA",
                        Items:
                        [
                            new(14, "Seasonal Basket", 2, [seasonal, retail])
                        ])
                ]),
            new(
                Id: 2,
                Name: "Dan Analyst",
                Posts:
                [
                    new(
                        3,
                        "Vol Surface Recap",
                        [
                            new(4, "Gamma supply is back", "Eve")
                        ])
                ],
                Orders:
                [
                    new(
                        Id: 5001,
                        CreatedAt: new DateTime(2026, 3, 6, 10, 0, 0, DateTimeKind.Utc),
                        Total: 310m,
                        Status: OrderStatus.Active,
                        Reference: "P-EPSILON",
                        Items:
                        [
                            new(15, "Retail Collar", 1, [retail])
                        ])
                ])
        ];
    }
}
