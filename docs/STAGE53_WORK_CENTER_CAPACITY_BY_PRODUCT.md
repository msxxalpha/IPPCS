# Stage 53 — Product-specific work-center capacity

## What the work-center form now supports
The **Mراكز کاری** area in `/app.html` retains the existing general nominal reference rate for the work center. A new section below the work-center list separately stores an optional throughput override for a **product × routing revision × operation × work center**.

To add a rate:
1. Open the main application and choose **۱. داده‌های پایه کارخانه → مراکز کاری**.
2. In **ظرفیت محصول‌محور هر مرکز کاری**, select a finished or semi-finished product.
3. Select the work center. The operation selector then shows only operations on that center for the selected product and a non-superseded routing revision.
4. Select the exact operation and enter the expected processed units per hour. Choose whether the override should be active in planning and save.

There is one override per routing operation. If a product has several different operations—even on the same work center—set a separate rate per operation. The list shows the product, center, revision, operation, rate, equivalent minutes per processed unit, active state and last change actor/time.

## Meaning of the number
The custom rate is **processed operation input units per hour**, not a claim of saleable finished units per hour for the entire route. The persisted Routing operation remains the reference for setup minutes and yield. When an active override is selected, the effective operation run time used in planning is:

`runMinutesPerUnit = 60 / capacityUnitsPerHour`

The CRP and finite APS/lot-capacity calculations account for the product's routing and cumulative downstream yield losses. The UI shows whether CRP uses a product override or the routing standard. Deactivating an override retains its record and makes planning fall back to the saved Routing run-time standard.

## Where it is applied
- Product-dependent CRP output and capacity load.
- Aggregate rough-cut CRP used for overload and MPS change-impact comparison.
- S&OP rough-cut capacity and finite APS scheduling.
- Net MRP suggested-lot capacity preview.

Rates are read from the internal IPPCS SQL Server database. Updating an override does not overwrite approved Routing standards. It is limited to a real Routing operation and the linked center/product, uses an internal audit actor/time, a unique operation key, and a SQL positive-rate constraint. This is planning information only; it does not raise shift-calendar minutes, change machine reservations, prove actual equipment availability or authorize production release.

## Scope and acceptance
The general center reference rate is preserved for existing data and users. No warehouse, purchasing, sales, HR, external ERP or calibration integration is introduced. The software tests validate the model and formulas; a factory representative still needs to verify each product-operation rate against measured production evidence and approve the applicable Routing/quality methods before using it for commitments.
