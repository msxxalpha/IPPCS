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
| 02 | Manufacturing demand inputs and forecasts | Partial: internal dated demand, Jalali-month moving average (confirmed history), duplicate-safe draft-MPS intake and Persian UI; forecast calibration pending |
| 03 | S&OP production balancing | Partial: 90-day total and per-center demand/load vs shift capacity, stored snapshots and bottleneck warnings; S&OP approval and feasible load leveling pending |
| 04 | MPS master production schedule | Partial: MPS version, date-specific finished-item lines, approval/locking and Persian forms |
| 05 | Multilevel net MRP and pegging | Prototype net MRP: manual balance/receipt assumptions, safety, reservations, quarantines, lot sizing, lead time, multilevel BOM, immutable calculation snapshots; read-only proposed-batch net demand graph and BOM-origin allocation implemented; persisted manufacturing-order reservation/pegging pending; gross BOM source paths plus read-only date-ordered planning-balance and expected-receipt attribution available; net suggestion batch-uplift/release warnings now validated and displayed |
| 06 | CRP finite and infinite capacity | Rough-cut daily preview plus finite no-overlap work-center allocation preview for net MRP lots, honoring component-batch completion and release dates; active IPPCS work orders conservatively block unfinished-operation centers; no actual timed resource reservations |
| 07 | APS constraint-based scheduler | Finite nonoverlap and operation precedence per MPS line, plus separate release-date-aware net MRP lot preview; shift-capacity calendar, scenario-specific Jalali work-center maintenance blackout days in MPS APS, Gantt and warnings; cross-lot planned manufactured-component readiness now checked in hypothetical preview; exact live work-order machine intervals, partial sub-lot material readiness, alternate tooling and global optimization remain pending |
| 08 | Production order lifecycle | Partial: draft/release/hold/resume/cancel/completion, routing and BOM snapshots, order audit |
| 09 | Semi-finished shop-floor control | Partial: sequential confirmations, good/scrap/rework, quality-adjusted WIP flow; no stock movement |
| 10 | Final shock absorber assembly execution | Partial: approved finished-item work order + operation progress |
| 11 | Line-side material planning and consumption | Partial: manual local consumption versus approved BOM quantity and variances; no warehouse transactions, physical reservations or backflush |
| 12 | Plan adherence and deviation management | Partial: MPS version vs completed final good output by product and due-date range; partial explicit quantity-based MPS-line-to-order allocation and fulfillment; root-cause, corrective-action, accountable owner, Jalali due date and auditable closure workflow now in prototype; UAT pending |
| 13 | In-process quality controls / quarantine | Partial: approved dated Control Plan, operation-specific numeric characteristics, frozen work-order checks, mandatory inspection gate, out-of-spec quality hold and immutable measurement history. Sampling rules/SPC pending |
| 14 | OEE and downtime classification | Preliminary daily OEE from manually planned minutes, reported output and downtime; advanced time/PLC validation pending |
| 15 | Manufacturing lot/serial traceability | Partial: exact lot history plus auditable quantity-limited links between adjacent operation confirmations of a work order; cross-order component genealogy pending |
| 16 | What-if simulations and scenarios | Partial: side-effect-free baseline/scenario finite-capacity multipliers and scheduled/late/unscheduled comparisons |
| 17 | Conversion cost and productivity analysis | Partial: user-entered internal center rate assumptions, standard-cycle conversion estimate and scrap-cost estimates; no actual ledger/cost-accounting integration |
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
