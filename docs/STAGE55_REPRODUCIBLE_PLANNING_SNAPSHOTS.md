# Stage 55 — reproducible MRP, S&OP and APS calculation inputs

## Purpose

A planning result is only auditable when the application retains the assumptions that produced it. This increment closes two evidence gaps discovered after product-specific work-center capacity and BOM alternatives were added. It changes stored calculation snapshots and regression contracts only; it introduces **no database migration** and does not modify approved Routing/BOM master data.

## MRP — preserve exact BOM alternative semantics

The persisted MRP input snapshot now includes each BOM line's stable line ID, component item, quantity-per, scrap percentage, sequence, alternative group code, default flag and priority. A reviewer can therefore distinguish:
- mandatory lines (alternative group code is null/blank);
- the single default option included in baseline gross/net demand; and
- approved substitutes that remain available for order-level allocation but are not double-counted as simultaneous MRP demand.

The calculation result and prior snapshots are not rewritten. This is an additive clarification of the input evidence for new runs.

## S&OP and APS — persist product-capacity evidence

Before product rates are applied to in-memory Routing operations, the application now captures an immutable evidence row for each relevant product/routing operation:
- item, routing revision and effective-date range;
- routing operation, work center, sequence and operation name;
- setup minutes and original approved Routing run minutes per unit;
- effective run minutes per unit used by the calculation;
- product capacity units/hour, when an active override exists;
- yield percentage and an explicit override/fallback flag.

S&OP stores only the capacity evidence for routing revisions that actually served valid demand rows. APS stores only the revisions represented by its scheduled task set, alongside the existing task durations, material-feasibility result, shifts, reservations and scenario restrictions. The same evidence is returned with the immediate calculation response so the planner can inspect the assumptions without parsing the saved run.

The evidence is built before ApplyToRoutes changes the disposable in-memory route objects. Approved Routing standards remain unchanged. An inactive or absent product override is explicitly identified as fallback to Routing.

## Regression coverage

- xUnit proves the saved evidence keeps original Routing run time separate from the effective product override and proves evidence capture does not mutate the route.
- Browser/API source-contract regression checks that MRP snapshots retain all BOM alternative fields and that S&OP/APS persist product-capacity evidence.
- The existing product-capacity, MRP alternative, UI, SQL migration and authenticated production test chains remain in place.

## Scope and release boundary

No schema change, destructive operation, warehouse/purchasing/sales/HR integration or external stock posting is introduced. The work stays on feature/production-platform-foundation; the frozen uat/first-factory-test-2026-10-28 branch and main are not changed.

This is stronger calculation provenance, not factory acceptance. The 18-module on-site operator/process gates, local Visual Studio/SQL validation, IIS/security review, backup/restore rehearsal and release authorization remain mandatory. Do not promote the Draft PR solely because these snapshot checks are added.
