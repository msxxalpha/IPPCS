# IPPCS developer setup

## Working branch
`feature/production-platform-foundation` (Draft PR #1); `main` remains unchanged until review.

## SDK and database
Use .NET 10 SDK and a dedicated development-only SQL Server database. A SQL Server connection string is not stored in Git:
```sh
cd src/IPPCS.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Production" "Server=localhost;Database=IPPCS_Dev;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet tool install --global dotnet-ef
dotnet ef database update
dotnet run --environment Development
```
Do not run `EnsureCreated` against a persistent DB. The initial EF Core migration was generated in CI and committed under `src/IPPCS.Web/Migrations/CI/`, including ASP.NET Identity tables. Review SQL/database backup strategy before applying to any persistent database. SQL Server integration workflow (`.github/workflows/sql-schema.yml`) exercises applying the migration to a disposable database; it is NOT a substitute for local UAT on the target SQL Server.

## Browser
Visit the development URL root. It redirects to `/app.html`. The responsive RTL UI supports item catalog, work centers, multilevel BOM, routing versions, routing operations, work shifts, and simple prototype production plans.

## Prototype restrictions
The system now includes a development-only local ASP.NET Core Identity login with role-gated API, cookie expiration, rate limiting and CSRF on mutations. No default credentials are embedded. The initial admin is created only when Bootstrap credentials are supplied and SQL schema is ready. Do not set a real password in repository files or share credentials in chat. Granular department/module permissions, security acceptance and IIS checks are still outstanding. The host deliberately refuses non-Development environments. Do not expose the developer port to untrusted networks.
Inventory management, purchases, sales, employees and **all integrations to those systems** are explicitly out of scope. Optional future user-approved read-only item master view is not connected.

## Modules and advancement
Use `docs/SCOPE.md` for all 18 modules and actual completion status. Never label gross requirements as net MRP, shift master data as CRP, or simple production plans as full MPS. Plan version approvals, labor standards, material reservation, BOM effectivity, and scheduler integration are forthcoming.

## Stage 3 planning workflow (manual assumptions only)
1. Define items and work centers at `/app.html`.
2. Define and approve routing operations and shifts.
3. In `/planning.html`, define versioned BOM for finished/semi-finished items. Approve only non-overlapping revisions.
4. Create MPS version, add finished-product lines using Jalali picker, approve to lock.
5. Enter dated baseline planning balances for **every demanded/derived item**, and any dated expected receipts **after** baseline date. All balances must share a common baseline date.
6. Run net MRP; snapshot stores input and outputs; CRP displays daily rough-cut overload only.

The MRP engine deliberately refuses to guess unknown item balances/lead-time policies, use cyclic or absent approved BOM structures for manufactured items, or double count scheduled receipts before the stock snapshot. Historical gross-BOM preview under the old `/app.html` page is separate and is not automatically included in versioned-BOM MRP. True finite-capacity scheduling, subcontracting, alternate BOM selection and comprehensive pegging remain later milestones.

## Stage 4 manufacturing execution workbench
1. Open `/login.html` to log in using the development-only administrator username/password configured through user secrets.
2. Open `/app.html` to define semi-finished or finished items, work centers, routing operations and work shifts.
3. In `/planning.html`, create and approve an effective-dated BOM revision, covering the planned order start.
4. Open `/execution.html`; create a work order specifying internal order number, item, planned quantity, start and due dates (all dates selected using shared Jalali calendar).
5. Release the draft work order. The exact approved routing operations are copied to immutable order operation records.
6. Confirm good, scrap and rework quantities by operation in sequence. A later operation cannot consume more than previous operation's good output; the sum of dispositions never exceeds input quantity. Full scrap at an upstream operation closes zero-input downstream steps.
7. Optional: place order on hold and resume, log production downtime, or inspect audit/history.
8. The final operation's good quantity is shown as the order's good output. There is no stock transaction or purchasing operation.

### Controls and known limitations
- The release action verifies the selected BOM and routing approvals and their effective validity.
- The `RowVersion` concurrency token protects order state updates during confirmation; database-level runtime concurrency has not yet been validated with two parallel operators.
- Lot labels on confirmations are basic references, not end-to-end traceability.
- Current shop-floor workflow requires full completion of a preceding operation before confirming a subsequent step. Partial transfers, combined line throughput, scrap disposition approval, NC/rework processing, line material backflush and OEE remain future features.
- Role-gated APIs do not imply granular role-based privileges for planners versus operators. Only Administrator is bootstrapped currently.
- All data lives in dedicated IPPCS SQL tables. No existing warehouse/purchasing/sales/HR APIs or SQL views are called.

## Checked-in initial database migration
- The initial migration currently carries the generated name `20261026184038_CiSchemaSmoke`; it is the baseline schema, not an ephemeral runtime-only migration.
- Visual Studio users should perform only `dotnet ef database update` against an empty **IPPCS_Dev** database after configuring the development connection string.
- Every additional entity/model change must ship a reviewed incremental EF migration. The build workflow runs `dotnet ef migrations has-pending-model-changes` to prevent silent DbContext/SQL drift.
- A separate SQL Server integration workflow starts a disposable SQL Server container with generated test-only credentials and attempts to apply the actual committed schema.
- If a database already has nontrivial business data, test and back up before running a migration; no destructive rebuild is authorized.

## Stage 5 quality/WIP/OEE
- New `/execution.html` tabs: Quality + WIP, and preliminary OEE.
- When an operation produces good units, an active order can place an unconsumed subset on a quality hold. Later operations are blocked while predecessor quality holds remain open. A quality inspector/administrator resolves a hold into accepted or quality-scrapped output, under an audited operation record. The released balance is not a warehouse inventory item or warehouse transfer.
- WIP is **derived** from manufacturing operation confirmations minus unresolved holds, quality scrap, and work already reported at the next operation. It is an in-process flow view, not a new warehouse or material stock ledger.
- OEE uses a manually entered planned-production-minutes assumption per work center and Persian date, timestamped shop-floor confirmations and downtime events. Its values are provisional until shift calendar, ideal cycle data and event timestamp validation are fully deployed.
- OEE is calculated by local factory date in timezone `Asia/Tehran` (config key `Production:TimeZoneId`). Persist actual event timestamps as UTC.
- Granular policies: `IpQualityApprove` for disposition, `IpOeePlanning` for planned production minutes, and `IpProductionUser` for reading shop-floor/quality/OEE. Only administrator bootstrap is currently available; broader role and user administration needs further work.

### Lot code search
The `/execution.html` lot-history tab searches exact lot identifiers recorded on operation confirmations. It provides order, operation, timestamp, good/scrap/rework and notes **inside IPPCS only**. It is not supplier material lot genealogy or finished serialized shock-absorber traceability.

### Database stage-five migration
The committed additive migration `20261026190313_StageFiveQualityOee` introduces `ManufacturingQualityHolds`, `ManufacturingOeePlans`, and two quality quantity columns on existing work-order operation records (0 defaults). The initial schema remains untouched; apply using `dotnet ef database update` only after reviewing the incremental changes and backing up persistent data. CI verifies migration application against disposable SQL Server, followed by authenticated shop-floor and quality workflow smoke tests.

### OEE limitations
The current dashboard uses the UTC confirmation/timestamp converted to plant local day (`Asia/Tehran`). Stoppage duration is assigned to the date it was recorded, not allocated across actual start/end timestamps. Quality scrap decided after a recorded good production confirmation is **not yet retroactively applied to historical OEE quality**. For this reason OEE is a provisional engineering indicator, not yet a controlled corporate KPI. Shift reconciliations, overlap-safe downtime intervals and subsequent quality dispositions need further design.

## Stage 6 — Control Plan and adjacent-operation lot lineage
- Open `/control-plan.html`; create a draft Control Plan for an **approved routing revision**, with effective Jalali from/to dates and a unique revision code.
- Add numeric characteristics for existing routing operation sequence numbers; supply an engineering description, unit of measure, min/max acceptance criteria and mandatory flag. Approve only after review. Approval prevents adding more characteristics to the same revision. Overlapping approved plans for one routing are rejected.
- When a **new** work order is released, the active approved Control Plan's characteristics are copied to that order's operations (with source characteristic IDs). The old work orders do not retroactively receive new requirements.
- After recording a production confirmation, an authorized inspector enters a measurement. An out-of-range numeric result must specify a nonconforming quantity; the system automatically raises a quality hold. Quality disposition (accepted/scrapped) is audited and distinct from the measurement. A required gate must have a passing measurement, and no upstream pending quality quantity may remain, before the next operation can receive output.
- The final work order can only reach Completed when the required final-operation inspections pass.
- The genealogy tab pairs two already-recorded operation confirmations **within the same production order**; the source/destination must belong to consecutive routing operations and quantities cannot exceed independently unallocated good outputs. It creates no physical stock movement, no consumed materials and no cross-order supplier genealogy.
- No Control Plan existing for a routing still allows the legacy prototype flow, for backward compatibility. **Mandatory Control Plan configuration for all actual production items** is a future deployment gate; do not treat the prototype as a released IATF quality control system.
- CI integration test runs approval, snapshot, hold, inspection fail/pass, scrap disposition, final inspection and lot genealogy on disposable SQL Server. The migration `20261026193011_StageSixControlPlan` adds Control Plan, characteristics, inspection requirements/results, and within-order batch links; review/backup before applying on persistent databases.

## Stage seven — production-only demand, S&OP, finite scheduling, what-if and internal costs
- Open `/planning-advanced.html`. Create dated independent demand using the shared Jalali picker; no integration is made to sales or other systems.
- Run rough-cut S&OP to save a demand/shift snapshot. The capacity is aggregated across work centers and must not be treated as a constraint-feasible production plan.
- For the actual finite scheduling preview, first prepare a **single approved MPS** with active approved routings and valid weekly shift calendars. Select a Jalali start date and 1–90 day horizon. The greedy non-preemptive algorithm places each operation within a single work-center shift, protects nonoverlap and the previous operation's finishing time, flags lateness, and marks unschedulable jobs explicitly; it does not reserve materials or optimize all orders globally.
- Capacity What-if recomputes baseline and modified effective shift windows in memory and never changes order or approved MPS records.
- Record line-side manufacturing material usage only while a production order is active and only for components in that order's frozen BOM revision; a consumed quantity affects only IPPCS tracking, **not warehouse stock**. Compare usage with BOM-based expected quantities.
- Enter development-only center conversion cost and scrap assumptions in **rial units**. Cost results are standard-time * assumed rate only, not actual invoices, payroll or finance data.
- Operational dashboard displays total order, downtime, hold, completed good and MPS-fulfillment signals; filters, user permissions, material-ready pegging, audit data versioning, and production UAT remain incomplete.
- Additive stage-seven migration `20261026194722_StageSevenFactoryPlanning` creates `FactoryDemands`, `FactorySopReviews`, `FactoryApsRuns`, `FactoryMaterialUsages`, `FactoryCenterCostRates` without destructive table operations. Back up persistent data and review SQL scripts before applying.

### Stage-seven refinements
- The independent demand page can transfer a selected need to a **draft** MPS version. Repeating the same transfer to that version is rejected; it does not touch the sales module or any external database.
- The simple Jalali moving-average forecast only uses prior `Confirmed` internal demands; zero-demand months are included. It is an advisory preview, **not** a verified forecasting model.
- S&OP now displays overload by individual work center, so spare capacity at one center does not hide a bottleneck at another. This remains a coarse aggregated review, not date-scoped optimal schedule.
- APS has a date- and center-specific Gantt bar view. It remains nonpreemptive and does not reserve materials or account for alternate work centers.
- Dedicated `IpPlanningWrite` policy limits new demand/S&OP/APS/scenario state changes to Administrator/Planner and `IpShopFloorWrite` limits line-side consumption writes to Administrator/ProductionOperator. Older prototype endpoints still require broader permission refactoring.
- The complete factory acceptance backlog is in `docs/ACCEPTANCE_MATRIX.md`.

## Standalone application account administration
- Log in as the development bootstrap Administrator and open `/system-users.html`. Create separate Planner, ProductionOperator or QualityInspector **IPPCS-only** credentials. No employee, national-ID, organizational role or HR portal fields are read or stored.
- Password policy: at least 12 characters with uppercase, lowercase, number and special character. Passwords are hashed by ASP.NET Core Identity, never included in returned user payloads.
- Any signed-in account can change its own password via the same page. Admin can lock/unlock operator accounts, but cannot lock the active Administrator account.
- Stage-seven authorization: Planner (or Administrator) for production demand input/MPS intake, S&OP/APS calculations and What-if; ProductionOperator (or Administrator) for shop-floor material consumption; QualityInspector (or Administrator) for inspection/quality disposition. **Legacy endpoints still need a complete route-by-route role matrix before production acceptance.**
- Never share passwords in GitHub, logs, documents or this chat. Development/CI credentials are generated or injected via secrets; live passwords need organizational provisioning policies before UAT.

## APS shift unavailability in Persian dates
The `/planning-advanced.html` APS form now accepts a list of **Jalali-dated work-center blackout days**, each with a required explanation (for example, maintenance or energy curtailment). These are validated against the known factory work-center catalog and the selected 1–90-day planning horizon. The finite greedy engine skips those work-center days when scheduling operations; other centers can still run. The blackout input is recorded in the calculation snapshot and appears in the result. This is a **what-if planning restriction only**, not a persistent maintenance, equipment or downtime module. It does not change approved shift data and has no connection to HR, warehouse, maintenance ERP, purchasing or sales services.

Unit tests verify that maintenance moves a task to the next valid workday while respecting non-overlap. The scheduler additionally handles end-of-day time conversion without asking `TimeOnly` to represent an impossible 24:00 timestamp. All shutdown-date fields use the same shared Jalali input component as existing modules.

## Stage 8: MPS fulfillment and work-order linkage
Create and approve an MPS in `/planning.html`, then create a factory work order in `/execution.html`. Open `/mps-pegging.html` to link a matching work order to a specific MPS line, with a nonzero quantity. The server validates product ID, work-order due date, MPS approval and unallocated balances within a serializable SQL Server transaction; audit entries persist the provenance. Cancelled or already completed orders cannot gain new allocations. Reports show assigned quantities, production completed good quantity and remaining required amount. No inventory movement, purchase/sales or HR link occurs. This is not a replacement for multilevel material pegging. Run reviewed additive migrations after backing up existing data.

## Stage 9: production deviation lifecycle
In `/production-deviations.html`, choose an approved MPS version and the affected demand line; optionally choose a work order linked to that MPS line via `/mps-pegging.html`. The system rejects an unapproved line or an unlinked work order. Enter the reason category and short narrative. Next, provide root cause, corrective action, named responsibility (plain IPPCS text, **not HR data**) and a required Jalali follow-up date. Only cases with a defined action can be closed, after an auditable effectiveness/verification note. Closing is irreversible through the current API. The initial SQL schema is extended with additive `StageNineProductionDeviations` migration. The automated SQL Server workflow exercises create/reject-premature-close/assign/verify-close/read-history. Company-specific overdue escalations and sign-offs are still future acceptance criteria.

## MPS-sourced gross BOM demand trace (stage 9.1)
The `/planning.html` MRP tab now includes an explicit **gross material source trace** command. It lists the approved MPS line, root product, downstream component, BOM parent/revision, level, path and gross quantity including BOM scrap factors. Approved BOM variants are selected by MPS need date for this lineage view. Overlapping approved variants, BOM cycles, invalid component quantities and scrap policies fail validation. This diagnostic is **not** time-phased net MRP pegging or a reservation/allocation. It does not redistribute available inventory, lock suppliers or change factory transactions. Actual MRP release-date BOM selection and receipt-origin allocations remain separate future milestones. The endpoint needs no schema changes, and uses the same private IPPCS planning data.

## Stage 9.5: finite proposed-lot work-center capacity preview

1. In the MRP tab on `/planning.html`, choose an **approved MPS** and enter the desired horizon in days (1–90) plus a capacity multiplier above 0 and at most 2.
2. Select **کنترل محدودیت ظرفیت بچ‌های پیشنهادی MRP**. The server recomputes MRP and its proposed-lot demand graph from *IPPCS-only planning inputs*; it does not create an order or persist resource reservations. The read-only endpoint is `GET /api/v1/mrp/{id}/lot-capacity-preview?horizonDays=30&capacityMultiplier=1`.
3. Each proposed manufactured lot must have an approved routing applicable on its **release** date. The finite scheduler begins each lot no earlier than its release date, respects routing sequence and shifts, and avoids overlapping assigned operations per work center within this simulation.
4. Review blocked, late and unallocated lot rows. A proposed lot beyond the selected horizon or with a release before the opening-balance date is **blocked**, never silently moved into another day. All reported dates use the shared Jalali formatter. Scenarios do not change existing master data.

**Safety/accuracy scope:** proposed raw-material receipts and planning balances remain assumptions; the preview DOES NOT enforce physical material availability, scheduling precedence between separate manufactured parent/child lots, committed shop-floor work orders, maintenance or tooling downtime, shift splits, alternative resources, setup families, labor calendars or optimality. A result marked `OnTime` is a capacity-only hypothetical, never a release authorization. It is not approved for factory execution. No EF migration or external system integration is introduced. GitHub xUnit and disposable SQL E2E verify this functionality.

## Stage 9.6: scheduling manufactured material dependencies and live order contention

On the read-only net MRP lot capacity preview, a component demand may have a `ProposedManufacturingReceipt` as its source, with its `DemandKey` linked to `NetPegDemand.ParentLotKey`. That makes the source manufacturing lot an explicit predecessor of the consuming parent. The finite heuristic evaluates predecessor chains before dependent batches and uses the last confirmed hypothetical predecessor operation end time as the earliest parent start; failed/blocked predecessors mark the parent as unallocatable. Mismatched/cyclic lot graphs fail closed. GUID scheduling identities derive from the complete proposed lot key, rather than just `ItemId` and `ReceiptDue`.

`Released`, `InProgress` and `OnHold` IPPCS orders also affect the preview: every work center with **unfinished** operations is blocked from the order's planned start until the preview horizon end. Exact operating intervals are **not recorded** by those orders, so this is intentionally over-conservative and may reject actually feasible plans. The Persian preview summarizes the count of active orders considered; it does not write or alter any manufacturing order or reserve anything physically. Draft/cancelled/completed orders and already completed operations do not occupy these hypothetical centers.

Automated tests check child precedence, unavailable child propagation, cycle rejection, independent lot identities, existing order blocking and disposable SQL integration. **Still missing:** accurate order-slot reservations, automatic firm-order netting in MRP, cross-order genealogy, partially available component lots, certified shift/maintenance calendars, and physical material feasibility. Never release production from this preview alone.

## Stage 9.7 — recording operation time (not an automated PLC source)

1. In `/execution.html`, choose **ساعت واقعی کارکرد مرکز**. Select a Released/InProgress/OnHold manufacturing order and one of its approved-route operations. Only `Administrator` and `ProductionOperator` can start/stop a clock. Operators must stop the active clock before confirming completion quantities. If the order is placed OnHold, clock-stop remains available.
2. Clock-start and clock-stop capture the trusted **server UTC timestamp**; the UI uses the Persian calendar with the explicit `Asia/Tehran` zone. No manual date fields or bypass to change a past observation. The events are appended to the existing audit journal with immutable operation/work-center identity, and can be retrieved by `GET /api/v1/manufacturing-orders/{id}/operation-clock`. Duplicate start, conflicting live center, out-of-order stop, invalid stage precedence and open-clock quantity completion fail closed.
3. The MRP capacity preview reads recorded sessions. Closed actual intervals occupy only their measured minutes (rounded outwards), crossing midnight when applicable. Open clock intervals block from actual start until the simulated horizon end. For active unfinished operations **without** an open timer, the previous conservative full-day block remains to avoid overstating available capacity. Read-only simulation; no reserved future dispatch slot is created.
4. This journal-based first implementation does not need an EF schema migration. It does not infer historical times from confirmation or stopwatch clicks retrospectively, and does **not** guarantee future machine availability. Production deployment still requires plant clocks/time sync, clock authorization UAT, performance/index review of audit queries, dispatch reservation modeling and downtime/OEE interval reconciliation.

## Stage 9.8 — allocating planned work-center time, with audit and actual comparisons

On `/execution.html`, choose **رزرو برنامه‌ای ظرفیت مراکز**. A `Planner` or `Administrator` selects a released or in-progress order and one of its unfinished operations, enters a date using the existing **Jalali datepicker**, selects same-day start/end clock times, then submits. The reservation must fit the work center's effective daily shift; the required length is at least the routing operation's standard setup-plus-run time. Operations scheduled out of sequence or overlapping other orders' center slots are rejected. Back-to-back end/start slots are valid (half-open semantics).

The API uses SQL Server's transaction-owned `sp_getapplock` keyed by work center before reading or writing the shared audit log; combined with serializable transactions this prevents two web requests from simultaneously booking one slot. Local shop-floor clock-start uses the same center lock and checks for an already-booked different operation at the present factory-local time. Creation and cancellation append immutable records in the existing manufacturing audit table; cancellation unlocks the planned slot but preserves history. A second cancellation is rejected. No EF migration.

Read `GET /api/v1/manufacturing-orders/{id}/capacity-reservations` for current slots and comparisons: planned minutes, actual closed-clock minutes, duration delta and start delta. If no actual clock exists, actual values remain null rather than manufactured. The MRP finite-resource preview blocks precisely the reserved minute intervals, together with actual recorded clocks; active unfinished operations that lack both a booking and a running clock still block conservatively.

**Important limits:** booking only one same-day shift interval per operation; not yet an official dispatch approval, no global optimizer, no conditional multi-machine alternatives, no reconciliation of future capacity after order cancel/complete except manual booking cancellation, no actual materials/PLC/worker integrations, no guarantee the booked operation will finish within the booked slot. The audit-based model needs scale/index and plant UAT before any production acceptance.

## Stage 9.9 — resource reservation lifecycle under concurrent operations

An IPPCS production confirmation runs under `IsolationLevel.Serializable` and obtains `IPPCS:OrderSlot:{orderId:N}` before loading the manufacturing order, validating the closed clock and applying quantities. When an operation is now complete, call `FactoryCapacityReservations.ReleaseOperations` while in the same transaction to append `CenterSlotCancelled` entries for just that operation's remaining slots. This also closes already-completed zero-input downstream routing steps. It stores an explicit `CapacityLifecycle` audit record with `OperationCompleted` or `OrderCompleted`. The database commits confirmation plus cancellations atomically.

Order cancellation uses the **same order lock**, refuses any open operation clock, and atomically appends cancellations of **all** outstanding slots before committing `ManufacturingOrderStatus.Cancelled`. Planner booking and cancellation, as well as clock start/stop, acquire the order lock before center-specific locks and mutable order operations. `sp_getapplock` is scoped to the active SQL transaction and shared across web processes. Center locks are acquired in deterministic Guid order where more than one center is affected.

`FactoryCapacityReservations.PendingLifecycleRelease` is a pure journal-replay selector; repeated lifecycle reconciliation is idempotent because already cancelled entries are absent from `Read`. Manual cancellation of an upstream step's slot is rejected when any unfinished downstream step still holds a slot. This is **no warehouse operation** and no physical machine completion is inferred.

Operational caveat: OnHold orders still reserve capacity conservatively; lifecycle-wide recovery and reconciliation on historically inconsistent legacy journals, maintenance calendars, PLC confirmation and operator UAT remain future acceptance work. The plan must not be used as approved plant dispatch without those controls.

## Stage 10 — four production reporting modules, no new database schema

- **Forecasting (02):** `FactoryProductionIntelligence.Backtest` calculates a monthly moving-average *separately* at each historical Persian-month cutoff; no later demand can influence an earlier prediction. MAE, WAPE and mean signed error use previously confirmed production demand only. A zero total actual produces `WapePercent=null`; sparse historical months are explicitly warned. Endpoint accepts only completed Persian months; date fields use the shared Jalali component.
- **MPS fulfillment (12):** `PeggedAdherence` joins the *approved* MPS line with `ManufacturingOrderPeg` and actual manufacturing orders. Proportional final-good attribution uses `min(peg.LinkedQuantity, finalGood * peg.LinkedQuantity / plannedOrderQuantity)` for completed, noncancelled orders. Completion UTC is localized to `Asia/Tehran` for on-time classification. If completion date missing, good is classified **undated**, not on time. Unassigned and pending amounts remain visible.
- **OEE (14):** the older manual-stop estimate remains unchanged. A **separate** `/oee/clock-verified` endpoint reads closed operation-clock sessions, clips their interval against Tehran-local day boundaries, and uses the existing daily shift-plan minutes plus actual confirmation units. Open running timers or absent required data produce null KPI. No second manual downtime subtraction, no PLC integration. The factory must still agree on whether operator clock constitutes true machine productive runtime.
- **Cost (17):** `/factory-cost/orders/{id}/actual-variance` reports standard-minute and closed-clock-minute quantities and rate-driven conversion costs, only when the order's planned start is on/after that rate's EffectiveFrom. Incomplete timers and missing/inapplicable rates give `null`, and overall totals remain null until every operation has coverage; never turn missing observation into zero rial.
- Both `/planning-advanced.html` and `/execution.html` show dedicated Persian report tables without changing existing behaviors, and the CI acceptance suite includes endpoint smoke checks. All four workflows are informational and read-only; they are not scheduling commitments, warehouse movements, accounting, or final site UAT.
