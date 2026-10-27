# IPPCS — سامانه مستقل برنامه‌ریزی و کنترل تولید کمک‌فنر

An independent manufacturing-specific planning and execution system built with .NET 10, SQL Server and Persian RTL user interfaces.

## Approved product contract
All **18 original manufacturing modules** remain in scope, tracked in [the module delivery ledger](docs/SCOPE.md).
Warehouse, purchasing, sales and HR modules are **excluded**; no live integrations with them exist.
Manual planning balances and expected receipts are IPPCS-only assumptions, not warehouse or purchasing transactions.

## Delivered development prototypes
- `/app.html`: items, work centers, simple BOM, routing revisions, shifts, gross requirements
- `/planning.html`: versioned BOM with Jalali effectivity, approved MPS, independent planning assumptions, net MRP prototype, rough-cut CRP
- `/execution.html`: semi-finished and final-assembly manufacturing orders, release and controlled lifecycle, operation confirmations, good/scrap/rework, recorded downtime and audit trail
- `/login.html`: independent local ASP.NET Identity login, account lockout, cookie expiration, CSRF validation and login rate limits
- Shared Jalali picker adapted from previous successful user projects. Server uses `PersianCalendar`.
- xUnit/Node automated checks and GitHub build + EF Core schema scaffolding.

## Stage 5: quality, WIP and daily OEE
- Production workflow now supports quantity-based quality quarantine and an audited accept/scrap disposition, with quality-inspector/admin approval and blocked downstream progression until quality resolves.
- Read-only in-process WIP availability is derived from operation results and quality holds; it is **not** inventory or a warehouse movement.
- Daily OEE preview uses local Tehran factory date, manually entered planned production minutes, captured quantities, reported downtime and standard routing cycle times. Incomplete timestamp validation, late quality dispositions and shift reconciliation can affect accuracy; not approved production KPI.
- Read-only exact-lot confirmation lookup surfaces in-system operations and work orders, but not a complete source-component genealogy.
- Incremental SQL migration `StageFiveQualityOee` is committed. Disposable-SQL GitHub integration tests cover quality hold, quality release, WIP and OEE in an authenticated work-order scenario.

## Stage 6: versioned Control Plan and manufacturing batch genealogy
- `/control-plan.html` provides a Persian RTL workspace to create and approve production-specific Control Plans based on a **routing revision** and Jalali effectivity dates.
- Numeric dimensional/force/process control characteristics include minimum, maximum, unit, stage sequence and mandatory flag.
- Releasing a new work order snapshots the approved applicable characteristics into immutable-order inspection requirements; approved plans and inspection history cannot be edited via the API.
- Downstream production and final order completion are blocked when mandatory inspection requirements are unpassed. Out-of-spec measurements create recorded quality holds. Quality disposition and a subsequent passing inspection are separate checks.
- The lot genealogy facility links **two adjacent within-order operation confirmations** and the quantity attributable to the source; it prevents duplicate or overallocated links. It is not cross-BOM material genealogy and makes no inventory movements.
- Automated tests now cover quality gates, specification boundaries, lot-link arithmetic and real SQL Server workflows.
- Incremental migration `20261026193011_StageSixControlPlan` is committed. Existing data and modules are retained.

## Stage seven: demand, rough-cut S&OP, finite scheduling and operational analytics
- New Persian RTL `/planning-advanced.html` with the reusable Jalali picker.
- Production-only demand categories (internal confirmed, forecast and safety build) without sales integration, a simple Jalali-month moving-average forecast (from previous confirmed internal needs only) and transfer into draft MPS with duplicate detection.
- Rough-cut 90-day S&OP capacity-load snapshots from approved routings and weekly shifts, including overload **per work center**. It is **not** an optimized time-phased balance.
- Finite, non-overlapping work-center allocations per approved MPS demand line, preserving routing precedence and effective shifts; no preemption or overlapping slots, and unscheduled/late warnings. A dated per-center Gantt view makes the schedule inspectable. This is a **heuristic APS prototype**, not a full optimizer with inventory, alternatives, maintenance, or setup-sequence constraints.
- Side-effect-free capacity what-if (different staffing/availability multiplier) without changing confirmed plans or external systems.
- Operation-side material consumption records and planned-vs-recorded BOM variances; not warehouse stock or warehouse receipts.
- Local **assumptions** of conversion cost rates in rials per center and standard-time estimates; not finance module or actual production-cost accounting.
- App-local `/system-users.html` for Administrator creation of Planner, ProductionOperator and QualityInspector credentials, lock/unlock, and all users' self-service password change, without HR or Active Directory integration. Planner/operator write permissions are separated on new stage-seven endpoints.
- Initial KPI dashboard and MPS completion variance overview.
- EF stage-seven migration adds five tables; unit tests exercise finite intervals, precedence, overcapacity, internal material consumption variance and plan fulfillment, and disposable SQL integration checks the endpoints.
- **All 18 agreed module categories now have at least an initial implemented capability**; none is being represented as factory-accepted or fully implemented.

## Implementation and security status
**Development preview only, not production-ready.**
- The stage-four shop-floor model tracks quantities and basic lot labels, but does not move physical stock, perform complete genealogy or calculate OEE.
- CRP is a rough-cut capacity preview, not finite resource scheduling.
- APS, advanced material pegging and several other promised modules have not been developed yet.
- Local authentication exists; granular per-module permissions, plant user management and security acceptance tests are still pending.
- The generated initial migration and incremental stage-five migration are checked into Git and checked for EF model drift. Practical SQL Server migration/UAT and IIS deployment acceptance remain prerequisites. Non-Development web hosting is blocked by design.
- Authenticated production APIs require CSRF request token for state changes.

## Acceptance and readiness
An explicit [18-module acceptance register](docs/ACCEPTANCE_MATRIX.md) identifies the factory UAT, security and business-process criteria that are **not complete**. Automated CI passing is not approval to deploy.

## Local developer setup
Use the [full developer guide](docs/DEVELOPER_GUIDE.md). Never put SQL passwords or bootstrap passwords in committed files. Configure user secrets and a disposable development database, apply a reviewed initial schema migration, then run `IPPCS.sln` with the **IPPCS.Web (HTTPS)** profile. Default landing is `/app.html`.

## Change control
Branch `feature/production-platform-foundation`, Draft PR #1. `main` is not modified until acceptance.

## Database schema verification
The committed baseline migration is located in `src/IPPCS.Web/Migrations/CI/`. GitHub CI enforces `has-pending-model-changes`, and a separate disposable SQL Server migration workflow lives at `.github/workflows/sql-schema.yml`. Follow the [developer guide](docs/DEVELOPER_GUIDE.md) rather than scaffolding another initial migration.

### APS maintenance-day restrictions
The APS preview now supports work-center-specific unavailability days entered with the shared Persian calendar. Such restrictions are local to each scheduling run, included in its stored input snapshot, and do **not** change actual shift master data or any existing maintenance/ERP application. See `docs/DEVELOPER_GUIDE.md` for setup and limitations.

## Stage 7.3 — APS material-feasibility preflight
Before any finite-capacity preview is stored, IPPCS checks internally entered planning balances, reservations, quarantine, safety stock, dated expected receipts available by horizon start, and approved multilevel BOM for every MPS line. The check accumulates demand so separate finished-item demands cannot use the same assumed material twice. Missing inputs or insufficient usable raw material result in an explicit 422 response and **no APS schedule is produced**. This intentionally conservative screen does not yet optimize replenishment timing or implement complete MRP pegging. No warehouse or purchasing system is connected. Automated pure-calculation and disposable SQL Server regression tests cover the new gate.

## Stage 8: explicit manufacturing order to approved MPS allocation
Persian page `/mps-pegging.html` connects existing work orders to specific approved MPS demand lines. Allocations enforce matching products, due dates, and remaining unallocated quantities on both sides, and create immutable audit events. MPS fulfillment now has an explicit line-level operational reference rather than only comparing aggregate items. This is **order pegging**, not yet full time-phased net-MRP component-origin pegging; that remains pending. Additional additive EF migration: `StageEightMpsOrderPegging`. No external ERP module or connector was created.

## Stage 9 — production plan deviations and root-cause workflow
The new Persian `/production-deviations.html` page links a recorded production variance to an **approved MPS line** and optionally one of its already-pegged manufacturing orders. Record the deviation category and description, assign a root cause, corrective action, responsible contact and Jalali action date, then close only after recording evidence of verification. Each transition is audited, and closed cases are immutable through the service. The additive migration `StageNineProductionDeviations` introduces deviation and history tables. These records are native to IPPCS, not an HR or corrective-action connector. See [18-module acceptance criteria](docs/18_MODULE_ACCEPTANCE_MATRIX.md) for the remaining conditions before production acceptance.

## Stage 9.2 — time-phased gross supply attribution (read-only)
On the MRP screen, `/api/v1/mrp/{mpsVersionId}/time-phased-sources` now attributes approved-MPS-driven multilevel BOM gross component demand to independent IPPCS **planning opening balances** or individually identified expected receipts available no later than the requested need date. Source quantities are consumed once in deterministic need-date order; unmet balances appear in a separate shortage report with the originating MPS line and BOM path. The calculation is a diagnostic preview and does not persist reservations or create warehouse/purchasing movements. **This is not full net MRP pegging**: planned manufacturing receipts, lot sizing, BOM selection by proposed release date and time-phased parent-order allocation remain unresolved. No EF schema change was needed.


## Stage 9.4 — net MRP proposal origin graph (read-only)
- `/mrp/{mpsVersionId}/net-demand-pegging` and the Persian MRP page expose individual time-phased MPS and proposed-manufacturing-batch component needs, dated source assignments, and proposed lots including whole-lot explosion, lead-time offsets, batch surplus and unused proposal quantity.
- Each proposed manufacturing lot generates children from the **approved BOM effective on its proposed release date**. A child links to its parent lot; allocations link MPS or component needs to dated input supplies and suggested lots. Input stock/receipts are internal assumptions; suggested external receipts are proposals, **not purchasing transactions**.
- The tracer reconciles gross demand, opening availability, net shortage and closing balances against existing net MRP rows; allocation cannot reuse a source quantity, or use future supply for earlier needs. Needs before the baseline are rejected instead of silently creating invalid material availability.
- This is an **auditable what-if graph** and does not reserve stock, issue orders, perform purchase/warehouse integration or calculate finite execution feasibility. The committed MRP result snapshot remains unchanged. Unit regression tests cover pegging, traceability, batch size, no-double-counting, release dates and invariant rejection.

## Stage 9.5 — finite work-center capacity preview for net MRP proposed manufacturing lots
- From the MRP tab in `/planning.html`, run `GET /api/v1/mrp/{approvedMpsId}/lot-capacity-preview?horizonDays=30&capacityMultiplier=1`. The horizon runs from the common manually entered planning-balance baseline, and the UI includes adjustable horizon (1–90 days) and assumed capacity multiplier (0–2].
- Unlike the prior finished-MPS-only APS proof of concept, this preview covers **net proposed finished and semi-finished manufacturing lots** from Stage 9.4, using the approved routing effective on each lot's proposed release date. It uses ordered routing operations, standard setup/run/yield time, work-center shift capacity and the existing nonoverlapping heuristic with an explicit earliest release date per proposed lot.
- A batch is flagged as in-time, late, blocked (invalid/absent route, release outside baseline/horizon), or unscheduled (insufficient capacity). The result is ephemeral, not a production authorization. All dates displayed in Persian are formatted with the shared Jalali date standard.
- Not modeled: confirmed material supply, **cross-batch material precedence**, current manufacturing-order capacity reservations, alternative machines/tools, maintenance schedules for this preview, setup sequencing, labor or optimized scheduling. This is NOT a safe-to-release approval and cannot replace plant dispatch planning.
- No EF model changes, external connections, or persisted state; xUnit + authenticated disposable SQL Server end-to-end checks included.

## Stage 9.6 — manufactured-component precedence and active-order capacity safety
- Finite MRP lot scheduling now follows the **actual proposed manufacturing receipt → consuming parent lot** allocations in Stage 9.4, scheduling required component lots first. Each parent operation starts only after its scheduled component lot's final operation is complete; a missing/unscheduled source prevents a misleading on-time parent. Cyclic material dependencies are rejected. Two lots sharing an item/date have distinct scheduling identities.
- Existing IPPCS manufacturing orders in `Released`, `InProgress` and `OnHold` count as **conservatively occupied work-center capacity** for unfinished operations. Because they lack per-operation production start/finish reservations, affected centers are blocked from the order's planned start through the **end of the preview horizon**, not guessed as precise occupied time slots. This can overstate load; explicitly show the number of active orders considered. Completed operations do not block.
- This is still **read-only and not an order-dispatch approval**: confirmed inventory/material consumption by work order, actual slot reservations, sub-lot partial readiness, genuine orders netted against MRP, optimized APS, tools and manpower remain pending. No SQL schema change or connection with ERP/warehouse/purchasing/sales/HR. Regression tests include direct child→parent dependency, unavailable child, duplicate item/date, active-order blocking, cyclic dependencies and authenticated SQL.

## Stage 9.7 — actual operation clocks and measured machine capacity
- The new **ساعت واقعی کارکرد مرکز** workbench on `/execution.html` records real operation start/stop through authenticated `POST /api/v1/manufacturing-orders/{id}/operations/{operationId}/clock-start|clock-stop`; no user-entered timestamp can forge actual duration. Operators and system administrators only. `GET /api/v1/manufacturing-orders/{id}/operation-clock` displays append-only audit-backed sessions with Persian local dates.
- Times come from the server's UTC clock and render as Asia/Tehran. Starting a clock requires an active, unfinished operation with completed upstream steps and quality gates. A center/order cannot have overlapping running clocks; duplicate starts, dangling stops and output confirmation while its clock runs are rejected. Already held orders may close an existing clock.
- Finite MRP lot capacity previews load **recorded actual center-occupancy intervals**. Intervals are rounded outward to minutes; an open clock occupies the center from its recorded start to the end of the simulated horizon. Unfinished operations without an open clock still receive the conservative full-day guard rather than being assumed available. A stopped clock is real historical usage, **not a future machine reservation**.
- The events reuse the immutable manufacturing audit table: no destructive SQL operations or EF schema/model updates. This improves traceability and measured intervals, but complete accurate **future** occupancy still needs explicitly approved dispatch slots, time synchronization, downtime reconciliation and factory UAT; it is not yet an MES/PLC measurement and does not imply production release approval.
