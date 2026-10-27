# IPPCS — criteria for completing all 18 modules

**Status:** Development, not approved for factory production. A module is *complete* only when its business rules, Persian RTL pages, granular role policy, database migration, unit + disposable SQL regression tests, and representative factory UAT have all passed. A basic menu, CRUD API or preview does not constitute completion.

| No. | Module | Implemented proof of concept | Major outstanding acceptance requirements |
|---|---|---|---|
| 01 | Factory master data | Item/work center/BOM and routing revisions, effective-date approval and shifts | Complete equipment/tool/resource hierarchy, robust revision changeover, approval ownership and import validation |
| 02 | Demand / forecast | Internal demand entries, Jalali monthly moving-average forecasts | Rolling historical MAE/WAPE/bias backtest implemented; seasonality, versioned accuracy thresholds, approval and import validation remain |
| 03 | S&OP | Finite horizon demand vs capacity and bottleneck snapshots | Cross-functional approval, iterative balancing scenarios and reconciliation to MPS |
| 04 | MPS | Approved version, dated lines, draft editing, demand intake | Release calendar, revision/lock governance, exception workflows and historical baseline |
| 05 | Net MRP / pegging | Multilevel net material plan, manual stock/receipts, lot sizing, MRP snapshot; separate order-to-MPS linking | Persisted work-order material pegging and execution commitment; the new **read-only** net planned-lot graph includes time-phased demand origins and named expected receipts, but remains unconfirmed and has no substitute selection or workday lead-time calendar |
| 06 | CRP | Rough-cut demand capacity; finite nonoverlapping shift allocation preview for proposed MRP finished/semi-finished lots with route release-date validity | Actual operation start/stop audit intervals and conservative active-order blocking exist; audited local machine reservation slots now implemented; still need lifecycle governance, synchronized clocks, labor/tool calendars, shifts spanning days and plant UAT remain |
| 07 | APS | Single-shift finite no-overlap engine; proposed MRP batches honor earliest release dates; existing MPS APS maintenance days/material preflight/Gantt | Hypothetical child-before-parent dependencies and recorded operation clocks exist; still requires firm material/order netting, plant-approved maintenance-aware dispatch slots, alternatives, shift splitting and plant validation |
| 08 | Manufacturing order management | Draft/released/in-process/held/completed/cancelled, route snapshots and state audit | Lifecycle role separation, integrated approval and authorized release/change control |
| 09 | Semi-finished execution | Ordered operations, good/scrap/rework, quality-adjusted WIP | Read-only gated WIP/operation readiness added; partial transfers, mixed lots, corrections/reversal governance and station transactions remain |
| 10 | Final assembly | Final-product work orders and quality-gated operation results | BOM-controlled origin lots from completed semi-finished orders now link to final assembly lots; model/variant selection, serial and end-of-line testing remain |
| 11 | Line-side material | Independent manual BOM consumption and standard/actual variance | Quantity reconciliation by operation/lot, reversals, component substitution rules and controlled backflush |
| 12 | Adherence / deviation | Basic MPS vs output plus **approved MPS-to-order quantity assignment**, deviation/root cause/action/closure | Per-line pegged on-time/late/unknown-date completion added; escalations, overdue alerts, evidence verification and factory UAT remain |
| 13 | In-process quality | Approved Control Plans, numerical tolerances, compulsory inspections, automatic holds and disposition | Per-characteristic historical pass/fail and readiness added, QC completion releases machine bookings; sample frequencies, SPC, MSA, reaction plans and calibration references remain |
| 14 | OEE | Daily availability/performance/quality preview and time duration entries | Completed operator-clock OEE and local-day clipping added; overlapping downtime reconciliation, PLC verification, shifts and retroactive quality scrap remain |
| 15 | Lot/serial genealogy | Exact lot lookup and bounded links between consecutive order operations | Audited cross-order semi-finished origin with quantity/BOM/quality checks added; multi-level recall drilldown, serial labels and corrections remain |
| 16 | What-if | Capacity multipliers and side-effect-free schedule comparison | Multiple constrained scenario dimensions, cost/material sensitivity, versioned assumption comparison |
| 17 | Conversion cost | Independent rates and standard cycle + scrap cost estimates | Recorded clock-time versus standard conversion variance and effective-rate guard added; rate history, overhead allocation, valuation rules and plant verification remain |
| 18 | Dashboards | Production, OEE, WIP, plan adherence and late-order previews | Access-controlled drilldowns, periodic trend, target/threshold alerts, exports and signed-off KPI definitions |

## Strict prohibitions
- Do not implement or connect warehouse, purchasing, sales or HR modules. The only future possible external source is an explicitly approved read-only item catalog query.
- Never silently rebuild/drop persistent databases. Every EF model change must ship additive reviewed migrations where possible, with backup and rollback rehearsal.
- All user-entered dates must use the shared functional Persian (Jalali) calendar; store normalized dates in SQL Server.
- Keep experimental changes in `feature/production-platform-foundation`; `main` stays unchanged until accepted.

## Current release gate
Until all relevant functional, cybersecurity, SQL Server/IIS, backup-restore, performance and factory user-acceptance tests pass, the Draft PR must not be merged or considered an approved production release.

## Stage 9.9 acceptance progress
- Auto-release of future capacity reservations when a routing step completes or a non-executed manufacturing order is cancelled, with preserved immutable audit and transaction atomicity.
- Shared order-level + center-level SQL Server application locks protect booking, live clock and manufacturing lifecycle mutations.
- Upstream slot cannot be manually cancelled while a later unfinished routing stage remains booked.
- Completed-operation and order-cancellation regression coverage added. **Still pending:** factory UAT, held-order dispatch decisions, field repair of legacy audit inconsistencies, plant authorization and full 18-module sign-off.

## Stage 12 verified incremental scope (not full acceptance)
| Module | Added read-only capability | Still needed for production signoff |
|---|---|---|
| 11 | BOM planned-vs-output standard-vs-logged consumption, nulls for absent logs | Operator-batch per-step reconciliation, approved substitutions, reversals, bounded backflush |
| 12 | Open/unassigned and overdue action alerts from existing recorded deviations | Action escalation owners, effectiveness evidence, signoff, notification policy |
| 13 | Exploratory individuals/moving-range limits on actual frozen Control Plan results; reject counts | Approved SPC baselines, MSA, sampling plans, rational subgrouping and reaction workflow |
| 15 | Bidirectional multistep work-order batch-link explorer with quantity edges and depth warnings | Serial-level trace and mixed-batch full genealogy, approved recall evidence, audited corrections |
| 18 | Reproducible Jalali-date alert dashboard for QC/order/deviation exceptions | Plant KPI targets, role-specific drilldowns, alerts policy, scheduled exports and UAT |

CI covers unit invariants and separate disposable SQL Server integration scenarios. **No** database or ERP schema exchange was added. Keep PR Draft; do not declare modules complete without factory UAT.
