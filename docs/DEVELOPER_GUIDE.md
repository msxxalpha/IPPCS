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
