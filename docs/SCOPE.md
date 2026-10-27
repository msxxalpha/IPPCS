# IPPCS — Approved scope and implementation register

## Hard boundaries
This is a standalone manufacturing planning and control system for automotive shock absorbers.
**Do not implement warehouse, procurement, sales, or HR modules.**
**Do not connect to their operational services/databases.**
Optional later input: user-provided, **read-only item catalog view/query**, with explicit approval. No automatic integration is currently implemented.
Planning inventory inputs, customer demand inputs, supplier lead times (if necessary for MRP) and labor capacity assumptions belong to IPPCS' own planning datasets or Excel imports, not modules for other departments.

## Contract: 18 main modules (all in scope)
| # | Module | Status |
|---|---|---|
| 01 | Production master data (items, BOM, routings, resources) | Partial UI + API: versioned BOM, routings, operations, centers, shifts; revision-approval checks |
| 02 | Manufacturing demand inputs and forecasts | Partial: internal dated demand, Jalali-month moving average plus historical MAE/WAPE/bias backtest for completed Persian months, duplicate-safe draft-MPS intake and Persian UI; accuracy calibration and data completeness acceptance pending |
| 03 | S&OP production balancing | Partial: 90-day total and per-center demand/load vs shift capacity, stored snapshots and bottleneck warnings; S&OP approval and feasible load leveling pending |
| 04 | MPS master production schedule | Partial: MPS version, date-specific finished-item lines, approval/locking and Persian forms |
| 05 | Multilevel net MRP and pegging | Prototype net MRP: manual balance/receipt assumptions, safety, reservations, quarantines, lot sizing, lead time, multilevel BOM, immutable calculation snapshots; read-only proposed-batch net demand graph and BOM-origin allocation implemented; persisted manufacturing-order reservation/pegging pending; gross BOM source paths plus read-only date-ordered planning-balance and expected-receipt attribution available; net suggestion batch-uplift/release warnings now validated and displayed |
| 06 | CRP finite and infinite capacity | Rough-cut daily preview plus finite no-overlap work-center allocation preview for net MRP lots, honoring component-batch completion and release dates; actual server-recorded operation start/stop intervals now inform preview; open clocks block to horizon end and active operations without clocks conservatively block full days; audited planned-operation work-center slots now added (not plant-authorized dispatch) |
| 07 | APS constraint-based scheduler | Finite nonoverlap and operation precedence per MPS line, plus separate release-date-aware net MRP lot preview; shift-capacity calendar, scenario-specific Jalali work-center maintenance blackout days in MPS APS, Gantt and warnings; cross-lot planned manufactured-component readiness now checked in hypothetical preview; plant-approved dispatch workflow and verified operation clock coverage, partial sub-lot material readiness, alternate tooling and global optimization remain pending |
| 08 | Production order lifecycle | Partial: draft/release/hold/resume/cancel/completion, routing and BOM snapshots, order audit |
| 09 | Semi-finished shop-floor control | Partial: sequential confirmations, good/scrap/rework, quality-adjusted WIP plus gated operation readiness for active orders; no stock movement |
| 10 | Final shock absorber assembly execution | Partial: approved final-assembly work order, step/quality readiness and explicit BOM-checked origin lot links from completed semi-finished production; no ERP interface |
| 11 | Line-side material planning and consumption | Partial: manual local consumption versus approved BOM quantity and variances; no warehouse transactions, physical reservations or backflush |
| 12 | Plan adherence and deviation management | Partial: aggregated product and explicitly pegged MPS-line adherence with attributed completed/on-time/late output, shortages and unassigned quantities; corrective-action/verification history remains; alerts and UAT pending |
| 13 | In-process quality controls / quarantine | Partial: frozen approved control plans, numeric checks, holds, dispositions, mandatory gate, historical per-characteristic pass/fail summary; order-scoped locks and QC completion capacity release. Sampling rules/SPC pending |
| 14 | OEE and downtime classification | Two clearly separated views: manually entered daily downtime estimate and clock-observed OEE (closed sessions, local-day clipping, invalid-data guards); PLC, overlapping stops and plant validation pending |
| 15 | Manufacturing lot/serial traceability | Partial: same-order operation genealogy plus quantity-guarded cross-order semi-finished-to-final-assembly batch links with BOM coverage. Serial/recall drilldowns and corrections pending |
| 16 | What-if simulations and scenarios | Partial: side-effect-free baseline/scenario finite-capacity multipliers and scheduled/late/unscheduled comparisons |
| 17 | Conversion cost and productivity analysis | Partial: standard conversion/scrap plus observed closed-clock minutes, effective-date rate and actual-vs-standard variance with null/missing coverage; no financial ledger or actual overhead attribution |
| 18 | Manufacturing KPI dashboards and decision support | Partial: OEE/WIP plus operational order, open-quality, stoppage, lateness, final good and MPS fulfillment previews; full drilldown/alerts pending |

## Architecture
ASP.NET Core web API / EF Core SQL Server. Modular monolith direction; this bootstrap will be split into domain/application/infrastructure modules as implementation expands.
Initial Persian RTL UI and shared Jalali datepicker implemented. Manufacturing order workbench at /execution.html; local Identity login and CSRF controls added. Dates are stored as Gregorian SQL date/time, with PersianCalendar server checks. Still pending: granular planner/operator authorization and user administration, plant-environment UAT, full WIP transfers/material allocations, serialized genealogy, Control Plan sampling and SPC, audited OEE, pegging and finite CRP/APS.

## Acceptance guardrails
- No claim of completion until user-visible screens, business rules, persistence, permissions and tests exist.
- No system may allocate quarantined material, use invalid BOM revision or double-book finite capacity.
- MRP must account for available inventory, reservations, due dates and lead times before being called net MRP.
- APS must respect precedence, calendars, finite capacity and resource eligibility before being called APS.
- Restore-ready backups and verified schema migrations are required before deploying against real data.

### Stage 12 — five additional manufacturing modules extended
- **11:** BOM-standard planned and final-stage output-scaled internal material consumption reconciliations; missing actual consumption remains null, never an assumed stock movement.
- **12:** pending and overdue corrective-action exception rows derived from existing audited deviations.
- **13:** exploratory frozen-characteristic historical individuals/moving-range control limits, pass/fail and sample-data guards; certified SPC sampling/MSA still pending.
- **15:** bounded forward/backward multistage genealogy across IPPCS work orders, with explicit evidence and depth-truncation indicator, not warehouse genealogy.
- **18:** Jalali-date-based operational alert feed for open quarantines, overdue/upcoming production orders and overdue/unassigned corrective action. Not an external notification module.
All five remain development-grade until representative factory UAT. No external warehouse, purchasing, sales or HR systems were connected or modified.

### Stage 13 — S&OP, MPS, MRP, CRP and What-if completion progress
- **03:** reconcile stored historical S&OP demand against a selected approved MPS and the current same-window aggregate; expose drift explicitly.
- **04:** audit explicit demand identifiers and unlinked/manual quantities in approved MPS by product and date. This is not an authorization or signed plan-lock feature.
- **05:** group net MRP allocations by source certainty (manual opening balance, expected receipt, unconfirmed manufacturing/external proposal); expose early-release risk and unmatched lot-size excess.
- **06:** rank work centers by time-phased overload and count days with no shift capacity; undefined capacity is never a fictional zero utilization rate.
- **16:** compare finite scheduling with capacity multiplier plus temporary center outage, without writing APS runs, orders, reservations or procurement.
All remain development/partial until factory UAT, data control, security hardening, performance and role-specific acceptance.

### Stage 14 — controlled approval and work-order lifecycle, operational dispatch

Modules **01/07/08** received integrated changes:
- 01: Protected effective-date BOM/Routing revisions with serializable global BOM-graph and per-routing-item approval locks, same-transaction validation, cycle/overlap/duplicate rejection and human-readable approval preflight API/UI.
- 08: Actual release enforces approved frozen revisions and approved MPS peg invariants under a shared order lock. Draft orders can be rescheduled with mandatory cause, checked effective revision rebind, before/after audit and prohibition after release.
- 07: Read-only Jalali-window dispatch queue gives quality-and-previous-step readiness, open holds, measured running clock, booked machine slots and missing-slot warnings without issuing production instructions.
No new database schema or integration with existing ERP warehouse/purchasing/sales/HR modules. All modules remain subject to factory validation.

### Stage 15 — line-side material and deviation-control workflow
- **11**: audited internal finished/semi-finished confirmation GUID lot usage constrained by frozen BOM ratios, standard-vs-actual lot report, legacy unattributed classification, and append-only equal/opposite-signed one-time correction with mandatory reason and shared work-order SQL lock.
- **12**: first-assignment-only corrective action; documented, immutable before/after action revisions; manual repeated-incident escalation and reproducible overdue case follow-up. SQL lock serializes competing case changes.
- **18**: exceptional escalation evidence raises overdue open-case priority and links KPI row directly to the in-portal deviation workbench.
No new EF schema, stock posting or connection to external systems. Formal 18-module acceptance remains gated by plant/UAT/security validation.

### Stage 16 — manufacturing handoffs, assembly and internal final QC
- **09**: adjacent-operation lot evidence and per-destination-lot missing-source report, conservative quality-rejected/held quantity gating. No physical transfer, warehouse or stock transaction.
- **10**: finished shock absorber batch-level BOM-proportional semi-finished provenance required for every final assembly lot at the internal acceptance gate, frozen after signature.
- **13**: completed operation sequence, passed mandatory frozen last-stage inspection, no unresolved QC hold and complete documented downstream/internal plus cross-order semi-origin prerequisites before append-only quality signoff. Only `IpQualityApprove` may sign, with shared SQL lock and audit.
All changes are additive to existing APIs and audit records; no data reset or new SQL tables. Await representative line-level factory UAT and remaining 18-module checks.

### Stage 17 — MPS version governance and auditable MRP/CRP change evidence
Modules **04, 05 and 06** now share a governed planning version lifecycle: an approved MPS can spawn an independently identified copy only with an explicit unique code and reason; original content remains unchanged. Every MPS writer uses a global `sp_getapplock` transaction lock to avoid approval/edit races. Draft-to-approved transition records a signed full-line snapshot. A controlled revision additionally requires successful same-transaction recalculation of net MRP and rough-cut CRP before the state can become Approved, preserving the complete demand/material/center-capacity difference as immutable JSON evidence. New Persian UI shows source history and before/after variance. No DB migration or external ERP integration is involved.
