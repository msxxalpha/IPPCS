# IPPCS — Manufacturing acceptance register (all 18 approved modules)

Scope is **manufacturing only**. Warehouse, purchasing, sales and human resources
are existing independent systems; do not implement, change or connect to them.
An external item catalogue is **not currently connected** and may only be consumed
from a specifically user-approved read-only SQL view in future.

## Status definitions
- **Prototype**: working API/UI or computation, but missing required use-case depth,
  site-specific configuration, evidence, or production acceptance.
- **Verified (CI)**: builds, unit tests and disposable SQL Server smoke scenarios pass.
- **Accepted (factory)**: signed process-owner UAT, representative data, reviewed
  quality and security controls, backup/restore and deployment evidence.
- All modules are currently **prototype**, even when CI verification exists.

| # | Module | Implemented stage | Factory acceptance required before completion |
|---|---|---|---|
| 01 | Production master data | Items, centers, shifts, dated BOM and routing revisions | Item revision lifecycle, product alternatives, engineering effective change, code lists and multi-shift calendars |
| 02 | Demand management and forecasts | Independent dated demand and Jalali-month moving average from internal confirmed needs | Forecast bias/error backtesting, manual override governance, scenario and version approval, bulk Excel import |
| 03 | S&OP production balancing | Dated rough-cut overall and center-specific load/capacity snapshots | Multi-center and time-phased conflict resolution, scenarios, sign-off and capacity commitment |
| 04 | MPS | Versioned and approved dated MPS; selected demand may enter draft | Allocation and reservation policy, approved demand-to-MPS reconciliation, change and exception workflow |
| 05 | Multilevel MRP | Time-phased net requirements, manual planning balances, safety, lot sizes, lead-time and run snapshots | Full root-demand pegging, alternate components, critical-material availability validation, shortage exception workflow and site data reconciliation |
| 06 | CRP | Route operation times, shifts, daily overload preview | Time-phased finite capacity and changeovers, overtime calendar and load leveling |
| 07 | APS | Finite nonoverlapping work-center slots, operation precedence, dated Gantt and overload warnings | Material readiness, alternate machines, downtime/maintenance, changeovers, setup families, split operations and optimization benchmarks |
| 08 | Production orders | Controlled creation/release/hold/continue/complete/cancel, audit and frozen routes | Process-wide order-to-MPS linkage, split/merge, authorization by function, cancellation/rework controls |
| 09 | Semi-finished execution | Operation-specific quality-gated quantities, scrap, rework and computed WIP | Operator handover between shifts, partial transfer, work-center and equipment traceability and returned rework flows |
| 10 | Final assembly execution | Final assembled quantity and mandatory test gates under Control Plan | Shock-absorber type-specific tests, re-test and station traceability, validated equipment interfacing |
| 11 | Line-side manufacturing material | Manual manufacturing consumption vs approved BOM, variance | Time/lot-scoped backflush, accountable WIP allocations, reversible audit and physical count reconciliation **without creating warehouse functionality** |
| 12 | Plan adherence | Versioned MPS vs final completed good units, overdue and plan variance | Root-cause capture, corrective action closure, MPS-line pegging and schedule adherence by shift |
| 13 | Production quality and SPC | Approved Control Plan, numeric checks, mandatory gates, quality holds | Control Plan sampling strategy, variable/attribute SPC, traceable calibration evidence, audits and NC escalation |
| 14 | Stops and OEE | Logged stoppage minutes, local-day OEE preview and planned-minute baseline | Exact stop time intervals, duplicate and overlap validation, late scrap reconciliation, OEE approval and shift reconciliation |
| 15 | Lot genealogy | Exact internal lot search and quantity-limited links between adjacent operations | Cross-order source/semi-finished trace, consumed component lot links, product serial numbers and recall query |
| 16 | What-if simulations | In-memory baseline vs capacity-change finite-scheduling scenarios | Saved scenario versions, multi-variable resource/material risk and management decision workflow |
| 17 | Conversion cost/productivity | Internal center conversion/scap rate assumptions, planned-vs-actual standard estimates | Rate versioning, conversion loss model, planned vs actual labor/equipment time, cost variance and sign-off |
| 18 | Production dashboards | Open order/quality, downtime, good output, fulfillment, OEE/WIP snapshots | Time-series dashboards, facility/shift KPI drilldowns, filters, alert thresholds, data freshness and rights |

## Shared hard gates
1. All date input widgets use the *same verified Jalali component* and
   `PersianPlanningDates` server-side validation; persist Gregorian date/date-time correctly.
2. No production-scope feature may perform an SQL write or API call in warehouse,
   purchasing, sales, or HR.
3. No incorrect stock counts, unapproved BOM/routing, unresolved quality hold,
   mandatory failed inspection, or double-booked center capacity may be silently accepted.
4. Preserve user data with reviewed, additive and reversible migration plans and
   verified **backup + restore**. Generate SQL deployment scripts for plant DBA review.
5. Role-specific application permissions, HTTPS and security testing, concurrency,
   audit trails, pagination and Excel import validation are prerequisites.
6. Unit and disposable SQL Server integration CI is necessary but **not sufficient**.
   Perform factory UAT for operators, planners, quality and management on realistic data.
7. A draft PR must not be merged or deployed while any mandatory acceptance criterion fails.

## Current release gate
`feature/production-platform-foundation` remains under Draft PR #1.
It is an early working multi-module baseline, **not 18 factory-accepted modules**.
