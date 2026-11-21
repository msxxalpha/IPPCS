# Stage 21 — Product-dependent production capacity and plan-to-execution feasibility

## Purpose

Make capacity planning reflect the product and its effective manufacturing route, rather than treating a work center as if it had one universal throughput. This stage is a planning/control enhancement only; it does not create warehouse, purchasing, sales, HR, maintenance, or calibration modules and does not connect to those systems.

## Required capacity semantics

1. **Capacity is a resource-time budget, not a product-independent unit count.** Work-center availability is derived from effective shifts, overnight carry-over, capacity percentage, and explicitly recorded local unavailability. Product demand is converted to required minutes through the approved routing revision effective for the proposed manufacturing lot's release date.
2. **Product/routing-specific load.** For each lot and routing operation, calculate setup minutes plus run time for the planned quantity using the effective operation standard, yield/scrap assumptions, and any explicitly configured product-specific cycle-time factor. Never fall back silently to the work center's nominal units/hour when an approved routing operation is missing or invalid.
3. **Separate time dimensions.** Keep setup minutes, run minutes, yield-adjusted quantity, planned available minutes, booked/occupied minutes, and unallocated minutes distinct. Do not combine capacity percentage and product efficiency multipliers in a way that double-discounts the same factor.
4. **Effective dating and frozen evidence.** The preview must identify the exact approved routing revision and capacity assumptions used per operation. A released manufacturing order retains its existing frozen route/rate evidence; a later master-data change must not rewrite historical load or order evidence.
5. **Shared-resource safety.** Existing active orders and valid capacity reservations must be treated as occupied capacity. Overnight operations consume capacity on every local production date they span. If the existing evidence is too coarse to identify precise slots, the preview must report the conservative blocked interval explicitly rather than imply a precise feasible slot.
6. **Finite sequencing.** A consuming parent lot cannot start an operation until every required manufactured-component lot is completed and the quantity is sufficient. A cycle, missing route, ineffective route, unallocated component requirement, or unscheduled child blocks the dependent operation and explains why.
7. **No hidden side effects.** A what-if preview must not create firm work orders, reserve capacity, post material consumption, mutate MPS/MRP snapshots, or create stock/purchasing transactions. Conversion to firm orders is a separate, explicit, audited action with idempotency and revalidation.

## Required result contract

For each proposed manufacturing lot and each routing operation, expose:

- item/lot identity, parent allocation where relevant, quantity, due date, proposed release date, routing revision, operation sequence, and work center;
- setup/run/total required minutes, local start/end date-time, capacity available and already occupied on each affected day;
- status: Feasible, Late, CapacityBlocked, MaterialBlocked, RouteBlocked, or HorizonBlocked;
- one or more structured blocker codes plus a human-readable Persian explanation;
- the earliest feasible start and projected completion, without pretending the estimate is an order commitment;
- a reconciliation summary proving that all required operation minutes are either allocated or reported as unallocated, with no double-booked interval.

The summary must aggregate load by **work center + local date + product/routing operation**, and also show bottleneck centers and late demand by MPS line. Capacity utilization above 100% must remain visible as overload, not be clipped.

## Acceptance scenarios

- Two products routed through the same center have different standard cycle times; their required minutes and feasible quantities differ accordingly.
- A routing revision effective after the proposed release date is not selected; a revision that expires before the release date blocks the lot.
- Setup time is charged once per planned batch, not once per unit; run time scales with the yield-adjusted quantity.
- A yield below 100% increases upstream required quantity while final good demand remains unchanged.
- A night shift crossing midnight consumes both local-date capacity buckets and prevents an overlapping next-day operation.
- An active order or reservation cannot be double-booked by a preview, including concurrent requests.
- A manufactured component scheduled late or short blocks the consuming parent with an explicit dependency reason.
- A missing/invalid route cannot be presented as feasible based on nominal work-center capacity.
- Capacity what-if changes only the simulation output; it does not change confirmed master data or reservations.
- Re-running the same preview with the same immutable inputs produces equivalent allocations and blocker codes.
- Concurrent duplicate conversion requests, stale MPS revisions, or newly occupied capacity are rejected after revalidation; no partial order set is created.
- Persian UI dates use the shared Jalali date component, while server calculations use the plant's Asia/Tehran local production calendar and correct overnight boundaries.

## Implementation sequence

1. Audit the current Stage 9.4–9.7 MRP pegging, Stage 9.5–9.6 lot-capacity preview, Stage 20 shift calendar, and reservation writers before changing calculation semantics.
2. Add pure, deterministic product/routing load calculations and unit tests first.
3. Add disposable SQL Server end-to-end tests for the existing real reservation/active-order locks, multilevel component precedence, and effective routing selection.
4. Extend the existing read-only preview and Persian UI with explicit daily load reconciliation and blocker details.
5. Only after preview invariants pass, implement a separate explicit, idempotent, audited conversion-to-firm-orders command with transactional revalidation. Do not merge or deploy based on CI alone.

## Release gate

This specification is not evidence that the stage has been implemented or factory-accepted. The current Stage 20 documentation reports that hosted CI terminates before build/test steps and provides no usable job logs. A green build and disposable SQL Server run on the exact candidate commit, followed by documented factory UAT, are mandatory before release.
