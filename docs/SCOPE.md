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
| 02 | Manufacturing demand inputs and forecasts | Not started |
| 03 | S&OP production balancing | Not started |
| 04 | MPS master production schedule | Partial: MPS version, date-specific finished-item lines, approval/locking and Persian forms |
| 05 | Multilevel net MRP and pegging | Prototype net MRP: manual balance/receipt assumptions, safety, reservations, quarantines, lot sizing, lead time, multilevel BOM, immutable calculation snapshots; pegging not implemented |
| 06 | CRP finite and infinite capacity | Rough-cut same-day capacity/load preview using approved routings and shifts, no finite allocation yet |
| 07 | APS constraint-based scheduler | Not started |
| 08 | Production order lifecycle | Partial: draft/release/hold/resume/cancel/completion, routing and BOM snapshots, order audit |
| 09 | Semi-finished shop-floor control | Partial: sequential operation confirmations with good/scrap/rework quantities |
| 10 | Final shock absorber assembly execution | Partial: approved finished-item work order + operation progress |
| 11 | Line-side material planning and consumption | Not started |
| 12 | Plan adherence and deviation management | Basic planned vs actual quantity in workbench; deviation reason workflows pending |
| 13 | In-process quality controls / quarantine | Partial: manual hold, scrap and rework dispositions; formal inspection/quality gates pending |
| 14 | OEE and downtime classification | Partial downtime event entry; OEE calculation not started |
| 15 | Manufacturing lot/serial traceability | Basic lot identifier on operation confirmation, no end-to-end genealogy yet |
| 16 | What-if simulations and scenarios | Not started |
| 17 | Conversion cost and productivity analysis | Not started |
| 18 | Manufacturing KPI dashboards and decision support | Not started |

## Architecture
ASP.NET Core web API / EF Core SQL Server. Modular monolith direction; this bootstrap will be split into domain/application/infrastructure modules as implementation expands.
Initial Persian RTL UI and shared Jalali datepicker implemented. Manufacturing order workbench at /execution.html; local Identity login and CSRF controls added. Dates are stored as Gregorian SQL date/time, with PersianCalendar server checks. Still pending: granular production role authorization, incremental production migration review and practical SQL Server runtime acceptance, full WIP transfers, WIP material movements, end-to-end traceability, quality gates, OEE, pegging, finite CRP and APS.

## Acceptance guardrails
- No claim of completion until user-visible screens, business rules, persistence, permissions and tests exist.
- No system may allocate quarantined material, use invalid BOM revision or double-book finite capacity.
- MRP must account for available inventory, reservations, due dates and lead times before being called net MRP.
- APS must respect precedence, calendars, finite capacity and resource eligibility before being called APS.
- Restore-ready backups and verified schema migrations are required before deploying against real data.
