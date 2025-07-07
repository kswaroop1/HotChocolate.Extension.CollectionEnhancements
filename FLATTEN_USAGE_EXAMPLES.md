# Flat Row Fields - Usage Examples

These examples are intended to be executable against the shared example domain
in `MISSION.md`.

They are normative acceptance-test queries. Generated flat fields use their
schema field names directly in selection sets and in flat-field arguments such
as `where`, `by`, `having`, and `order`.

## Single-Source Flat Query with Derived Prefix

```graphql
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
```

## Multi-Source Cross-Apply Flat Query

```graphql
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
```

This emits one row per reachable combination of underlying, coupon, and call
for a given security, with `id` and `isin` repeated on each row.

## Nested Local Flat Query

```graphql
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
```

This keeps the outer `people` shape and uses the generated flat sibling only
inside each person's `orders` context.

## Flat Query with Filter, Order, and Slicing

```graphql
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
```

The `where` clause uses the same generated schema field names as the flat row
selection set and must remain compatible with the requested `expand`. The same
flat rowset also supports `order`, `offset`, and `limit`.

## Flat Aggregate

```graphql
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
```

## Flat Grouped Aggregation

```graphql
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
```

## Root Flat CSV Export

```graphql
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
```

This is valid only at the root. The directive does not change the field shape;
it serializes the selected flat rowset as `text/csv`. The CSV header row is
`securityId;isin;paymentDate;rate`.

## Root Flat Group CSV Export

```graphql
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
```

For grouped exports, object selections such as `key` and `avg` are traversed to
their scalar leaves. With the aliases above, the CSV header row is
`paymentDate,groupCount,avgRate`.

## GraphQL Aliases Rename the Response Only

```graphql
query {
  securitiesFlat(
    expand: ["details.coupons"]
  ) {
    id
    payDate: couponPaymentDate
  }
}
```

The alias changes the response key to `payDate`, but the schema field remains
`couponPaymentDate`.

## Result-Field Alias

```graphql
query {
  exportRows: securitiesFlat(
    expand: ["details.coupons"]
  ) {
    id
    isin
    couponPaymentDate
  }
}
```
