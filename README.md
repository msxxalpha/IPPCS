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
