namespace HotChocolate.Extension.CollectionEnhancements.Tests.Specs;

public static class DocumentedSpecCatalog
{
    private const string PendingReason =
        "Pending collection enhancement implementation; keep this query aligned with the canonical docs.";

    public static TheoryData<ExecutableSpecCase> All { get; } = new()
    {
        PendingSuccess("A01", "Raw collection querying over a nested collection", """
            query {
              people {
                orders(
                  where: { status: { eq: ACTIVE } }
                  order: [{ createdAt: DESC }]
                  offset: 10
                  limit: 5
                ) {
                  id
                  total
                  createdAt
                }
              }
            }
            """),
        PendingSuccess("A02", "Single aggregate row with pre-aggregation filtering", """
            query {
              people {
                ordersAggregate(where: { status: { eq: ACTIVE } }) {
                  orderCount: count
                  totalSales: sum {
                    total
                  }
                  averageOrderValue: avg {
                    total
                  }
                  totalSalesStdev: stdev {
                    total
                  }
                  totalSalesStdevp: stdevp {
                    total
                  }
                }
              }
            }
            """),
        PendingSuccess("A03", "Higher moments on numeric fields", """
            query {
              people {
                ordersAggregate {
                  totalSalesSkew: skew {
                    total
                  }
                  totalSalesKurtosis: kurtosis {
                    total
                  }
                }
              }
            }
            """),
        PendingSuccess("A04", "Distinct count and string aggregation", """
            query {
              people {
                ordersAggregate {
                  uniqueReferences: countDistinct {
                    reference
                  }
                  orderRefsCsv: stringAgg(separator: ", ", order: [{ reference: ASC }]) {
                    reference
                  }
                  distinctOrderRefsCsv: stringAggDistinct(separator: ";") {
                    reference
                  }
                }
              }
            }
            """),
        PendingSuccess("A05", "Multi-member operator projection", """
            query {
              securitiesAggregate {
                countDistinct {
                  currency
                  isin
                }
                min {
                  price
                  expirationDate
                }
                max {
                  price
                  expirationDate
                }
              }
            }
            """),
        PendingSuccess("A06", "Grouped aggregation", """
            query {
              customers {
                ordersGroup(
                  by: [status]
                  order: [
                    { key: { status: ASC } }
                    { sum: { total: DESC } }
                  ]
                ) {
                  key {
                    status
                  }
                  count
                  sum {
                    total
                  }
                  avg {
                    total
                  }
                  stdev {
                    total
                  }
                }
              }
            }
            """),
        PendingSuccess("A07", "Grouped aggregation with having", """
            query {
              customers {
                ordersGroup(
                  by: [status]
                  having: {
                    and: [
                      { count: { gte: 2 } }
                      { stdev: { total: { lt: 50 } } }
                    ]
                  }
                  order: [
                    { key: { status: ASC } }
                    { count: DESC }
                  ]
                  limit: 10
                ) {
                  key {
                    status
                  }
                  count
                  sum {
                    total
                  }
                }
              }
            }
            """),
        PendingSuccess("A08", "Conditional count", """
            query {
              customers {
                ordersAggregate {
                  cancelledOrders: count(where: { status: { eq: CANCELLED } })
                  activeOrders: count(where: { status: { eq: ACTIVE } })
                }
              }
            }
            """),
        PendingSuccess("A09", "Aggregation over nested security collections", """
            query {
              securities {
                id
                details {
                  couponsAggregate {
                    couponCount: count
                    avg {
                      interestRate
                    }
                    stdev {
                      interestRate
                    }
                    stdevp {
                      interestRate
                    }
                  }
                }
              }
            }
            """),
        PendingSuccess("A10", "Parent filtering by nested aggregate criteria", """
            query {
              people(
                where: {
                  ordersAggregate: {
                    where: { status: { eq: ACTIVE } }
                    having: { sum: { total: { gt: 1000 } } }
                  }
                }
              ) {
                id
                name
              }
            }
            """),
        PendingSuccess("A11", "Parent filtering by grouped criteria", """
            query {
              customers(
                where: {
                  ordersGroup: {
                    by: [status]
                    having: { count: { gte: 3 } }
                  }
                }
              ) {
                id
                name
              }
            }
            """),
        PendingSuccess("A12", "Parent filtering by coupon count", """
            query {
              securities(
                where: {
                  details: {
                    couponsAggregate: {
                      having: { count: { gte: 3 } }
                    }
                  }
                }
              ) {
                id
                isin
              }
            }
            """),
        PendingSuccess("A13", "Parent filtering by date-bounded aggregate total", """
            query {
              customers(
                where: {
                  ordersAggregate: {
                    where: { createdAt: { gte: "2026-02-17T00:00:00" } }
                    having: { sum: { total: { gt: 1000 } } }
                  }
                }
              ) {
                id
                name
              }
            }
            """),
        PendingSuccess("A14", "Alias behavior for grouped aggregates", """
            query {
              customers {
                salesByStatus: ordersGroup(by: [status]) {
                  key {
                    status
                  }
                  rowCount: count
                  grossSales: sum {
                    total
                  }
                }
              }
            }
            """),
        PendingSuccess("F01", "Single-source flat field with derived prefix", """
            query {
              couponRows: securitiesFlat(
                expand: ["details.coupons"]
              ) {
                id
                isin
                couponObservationDate
                couponPaymentDate
                couponInterestRate
              }
            }
            """),
        PendingSuccess("F02", "Multi-source cross-apply flat field", """
            query {
              securityRows: securitiesFlat(
                expand: [
                  "details.underlyings"
                  "details.coupons"
                  "details.calls"
                ]
              ) {
                id
                isin
                underlyingRic
                underlyingCurrency
                couponObservationDate
                couponPaymentDate
                couponInterestRate
                callObservationDate
                callCallDate
                callIsCalled
              }
            }
            """),
        PendingSuccess("F03", "Nested local flat field", """
            query {
              people {
                id
                name
                orderTagRows: ordersFlat(
                  expand: ["items.tags"]
                ) {
                  id
                  total
                  status
                  tagTagName
                  tagCategory
                }
              }
            }
            """),
        PendingSuccess("F04", "Flat field with filter, order, and slicing", """
            query {
              activeCouponRows: securitiesFlat(
                expand: ["details.coupons"]
                where: { couponPaymentDate: { gt: "2026-01-01" } }
                order: [{ couponPaymentDate: DESC }]
                offset: 5
                limit: 10
              ) {
                id
                isin
                couponPaymentDate
                couponInterestRate
              }
            }
            """),
        PendingSuccess("F05", "Flat aggregate over expanded rows", """
            query {
              securitiesFlatAggregate(
                expand: ["details.coupons"]
                where: { couponInterestRate: { gt: 0.03 } }
              ) {
                count
                avg {
                  couponInterestRate
                }
                max {
                  couponPaymentDate
                }
              }
            }
            """),
        PendingSuccess("F06", "Flat grouped aggregation", """
            query {
              securitiesFlatGroup(
                expand: ["details.coupons"]
                by: [couponPaymentDate]
                order: [{ key: { couponPaymentDate: ASC } }]
              ) {
                key {
                  couponPaymentDate
                }
                count
                avg {
                  couponInterestRate
                }
              }
            }
            """),
        PendingSuccess("F07", "Generated-field alias behavior", """
            query {
              securitiesFlat(
                expand: ["details.coupons"]
              ) {
                id
                payDate: couponPaymentDate
              }
            }
            """),
        PendingSuccess("F08", "Result-field alias behavior", """
            query {
              exportRows: securitiesFlat(
                expand: ["details.coupons"]
              ) {
                id
                isin
                couponPaymentDate
              }
            }
            """),
        PendingSuccess("F09", "Root flat CSV export", """
            query {
              securitiesFlat(
                expand: ["details.coupons"]
                order: [{ couponPaymentDate: ASC }]
              ) @export(format: CSV, separator: ";", fileName: "coupon-rows.csv") {
                securityId: id
                isin
                paymentDate: couponPaymentDate
                rate: couponInterestRate
              }
            }
            """),
        PendingSuccess("F10", "Root flat-group CSV export", """
            query {
              securitiesFlatGroup(
                expand: ["details.coupons"]
                by: [couponPaymentDate]
                order: [{ key: { couponPaymentDate: ASC } }]
              ) @export(format: CSV, separator: ",", fileName: "coupon-groups.csv") {
                key {
                  paymentDate: couponPaymentDate
                }
                groupCount: count
                avg {
                  avgRate: couponInterestRate
                }
              }
            }
            """),
        PendingFailure("N01", "Invalid numeric aggregate usage", """
            query {
              people {
                ordersAggregate {
                  stdev {
                    reference
                  }
                }
              }
            }
            """),
        PendingFailure("N02", "Invalid flat expand path traversal", """
            query {
              securitiesFlat(
                expand: ["details.missingCoupons"]
              ) {
                id
              }
            }
            """),
        PendingFailure("N03", "Invalid generated-field selection for the requested expand", """
            query {
              securitiesFlat(
                expand: ["details.coupons"]
              ) {
                id
                callCallDate
              }
            }
            """),
        PendingFailure("N04", "Invalid flat where field usage for the requested expand", """
            query {
              securitiesFlat(
                expand: ["details.coupons"]
                where: { callCallDate: { gt: "2024-01-01" } }
              ) {
                id
                couponPaymentDate
              }
            }
            """),
        PendingFailure("N05", "Invalid flat aggregate or group field usage for the requested expand", """
            query {
              securitiesFlatGroup(
                expand: ["details.coupons"]
                by: [callCallDate]
              ) {
                key {
                  callCallDate
                }
                count
              }
            }
            """),
        PendingFailure("N06", "Invalid export target or nesting", """
            query {
              securitiesFlatAggregate(
                expand: ["details.coupons"]
              ) @export(format: CSV, separator: ",") {
                count
              }
            }
            """),
        PendingFailure("N07", "Invalid export operation shape", """
            query {
              securitiesFlat(
                expand: ["details.coupons"]
              ) @export(format: CSV, separator: ",") {
                id
              }
              customers {
                id
              }
            }
            """),
        PendingFailure("N08", "Invalid export separator or duplicate headers", """
            query {
              securitiesFlat(
                expand: ["details.coupons"]
              ) @export(format: CSV, separator: "||") {
                id
                duplicate: isin
                duplicate: couponPaymentDate
              }
            }
            """)
    };

    private static ExecutableSpecCase PendingSuccess(string id, string description, string query) =>
        new(id, description, query, SpecCaseExpectation.Success, SpecCaseStatus.PendingCollectionEnhancements, PendingReason);

    private static ExecutableSpecCase PendingFailure(string id, string description, string query) =>
        new(id, description, query, SpecCaseExpectation.ValidationFailure, SpecCaseStatus.PendingCollectionEnhancements, PendingReason);
}
