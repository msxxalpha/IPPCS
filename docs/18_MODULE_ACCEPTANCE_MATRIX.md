# IPPCS — criteria for completing all 18 modules

**Status:** Development, not approved for factory production. A module is *complete* only when its business rules, Persian RTL pages, granular role policy, database migration, unit + disposable SQL regression tests, and representative factory UAT have all passed. A basic menu, CRUD API or preview does not constitute completion.

| No. | Module | Implemented proof of concept | Major outstanding acceptance requirements |
|---|---|---|---|
| 01 | Factory master data | Item/work center/BOM and routing revisions, effective-date approval and shifts | Complete equipment/tool/resource hierarchy, robust revision changeover, approval ownership and import validation |
| 02 | Demand / forecast | Internal demand entries, Jalali monthly moving-average forecasts | Forecast accuracy/backtesting, seasonality, versioned approval and approved import flows |
| 03 | S&OP | Finite horizon demand vs capacity and bottleneck snapshots | Cross-functional approval, iterative balancing scenarios and reconciliation to MPS |
| 04 | MPS | Approved version, dated lines, draft editing, demand intake | Release calendar, revision/lock governance, exception workflows and historical baseline |
| 05 | Net MRP / pegging | Multilevel net material plan, manual stock/receipts, lot sizing, MRP snapshot; separate order-to-MPS linking | Persisted work-order material pegging and execution commitment; the new **read-only** net planned-lot graph includes time-phased demand origins and named expected receipts, but remains unconfirmed and has no substitute selection or workday lead-time calendar |
| 06 | CRP | Rough-cut load/capacity per day, shifts, approved routes | Full labor/tool/resource load, finite scheduling consistency, exceptions and cross-shift allocation |
| 07 | APS | Single-shift finite no-overlap engine, date effectivity, maintenance blackout days, material preflight, Gantt | Time-phased material availability for each operation, alternate machines/tools, optimization and baseline comparison |
| 08 | Manufacturing order management | Draft/released/in-process/held/completed/cancelled, route snapshots and state audit | Lifecycle role separation, integrated approval and authorized release/change control |
| 09 | Semi-finished execution | Ordered operations, good/scrap/rework, quality-adjusted WIP | Partial transfers, mixed lot and splitting, correction/reversal governance, operator station transactions |
| 10 | Final assembly | Final-product work orders and quality-gated operation results | Model/variant selection, serial and end-of-line test integration, serialized output genealogy |
| 11 | Line-side material | Independent manual BOM consumption and standard/actual variance | Quantity reconciliation by operation/lot, reversals, component substitution rules and controlled backflush |
| 12 | Adherence / deviation | Basic MPS vs output plus **approved MPS-to-order quantity assignment**, deviation/root cause/action/closure | Baseline vs actual schedule and quantities, escalations, overdue alerts, evidence verification and factory UAT |
| 13 | In-process quality | Approved Control Plans, numerical tolerances, compulsory inspections, automatic holds and disposition | Sample frequencies, SPC, MSA, reaction plans, calibration references and controlled forms |
| 14 | OEE | Daily availability/performance/quality preview and time duration entries | Actual timestamp intervals, overlapping downtime exclusion, shift reconciliations and retroactive quality scrap |
| 15 | Lot/serial genealogy | Exact lot lookup and bounded links between consecutive order operations | Cross-order semi-finished component origin, trace-all-parents and recall drilldown, serial labels |
| 16 | What-if | Capacity multipliers and side-effect-free schedule comparison | Multiple constrained scenario dimensions, cost/material sensitivity, versioned assumption comparison |
| 17 | Conversion cost | Independent rates and standard cycle + scrap cost estimates | Verified actual time, overhead allocation, valuation rules and actual vs standard variance |
| 18 | Dashboards | Production, OEE, WIP, plan adherence and late-order previews | Access-controlled drilldowns, periodic trend, target/threshold alerts, exports and signed-off KPI definitions |

## Strict prohibitions
- Do not implement or connect warehouse, purchasing, sales or HR modules. The only future possible external source is an explicitly approved read-only item catalog query.
- Never silently rebuild/drop persistent databases. Every EF model change must ship additive reviewed migrations where possible, with backup and rollback rehearsal.
- All user-entered dates must use the shared functional Persian (Jalali) calendar; store normalized dates in SQL Server.
- Keep experimental changes in `feature/production-platform-foundation`; `main` stays unchanged until accepted.

## Current release gate
Until all relevant functional, cybersecurity, SQL Server/IIS, backup-restore, performance and factory user-acceptance tests pass, the Draft PR must not be merged or considered an approved production release.
