# IPPCS — Factory acceptance execution pack (18 modules)
**Revision:** 2026-10-27 (1405/08/05) · **Status:** OPEN / NOT AUTHORIZED FOR LIVE USE.

This is a test execution and sign-off record, **not** proof that any scenario passed. Attach screenshots, exported outputs, anonymized results and named factory sign-offs to actual executions. Run first on a separated SQL Server/IIS test installation, never against a production company/ERP database. The four forbidden ERP modules (warehouse, purchasing, sales and HR) must never be built or connected. Item reference data must stay local unless a future explicitly approved *read-only* item view is supplied.

## Prerequisites (release-blocking)
- [ ] Runner/Actions account restriction resolved; all Build, xUnit, JS/Jalali, EF model-diff and isolated SQL scenario runs succeeded on the **same commit SHA**.
- [ ] SQL Server database backup + verified restore to a separate DB; no drop/rebuild, migration reviewed and rollback/recovery rehearsed.
- [ ] HTTPS/cookie/CSRF, least-privilege roles, rate limiting and lockout penetration smoke tests reviewed on staging IIS.
- [ ] All physical measurements are traceable to approved machine gauges, operator identity, lot, shift and correct Iran-local time; no unverified PLC/OEE claims.
- [ ] Engineering owner, production planner, shop-floor supervisor and quality owner signed representative results.
- [ ] No write, read or background connector to warehouse/purchasing/sales/HR apps or schemas.

## Factory scenario checklist — one signed test record per module
| No | Module / owner (confirm at plant) | Mandatory field test | Required evidence before acceptance | Current code-level status |
|---|---|---|---|---|
| 01 | Item, BOM, Routing / Engineering | Create old and new shock-absorber product BOM/Routing; overlapping ordinary approval fails; dated cutover preserves released work orders; only valid drafts are rebound | ECO signatories, correct ratios, fixture/changeover verification, revision audit | Versioned approval, impact, cutover + draft reassignment implemented; two-person ECO signing outstanding |
| 02 | Demand/forecast / Planning | Compare at least 12 closed Jalali monthly demand periods by model; measure WAPE, bias, and agreed exception thresholds | Historical input sign-off, targets, false-positive review | Moving average/backtest; forecast sign-off/seasonality outstanding |
| 03 | S&OP / S&OP owner | Run overload/bottleneck scenario and document product-mix acceptance into next MPS | Meeting decisions, approved capacity assumptions | Balancing/reconciliation preview; multi-role consensus outstanding |
| 04 | MPS / Planning | Version approval, freeze, revised-copy audit, exact demand-origin and previous version reconciliation | Signed production program and effective release horizon | Versioned controlled revision and signed-in planner restrictions; factory approval pending |
| 05 | MRP / Planning | Multi-level BOM netting and shortfall with manual *IPPCS internal* assumptions; verify linked demand pegging and material substitutions | Explain one calculation to physical material planner without ERP access | Net MRP + provenance; firm materials/alternates still missing |
| 06 | CRP / Production Planning | One day, Sunday→Monday 22:00–06:00 and 2 concurrent machines; no overbook or negative availability | Actual shift calendar and equipment/tool outage decisions | Overnight effective capacity and reservations; machine/tool UAT pending |
| 07 | APS / Scheduling | Candidate lots with parent-before-child, blackouts, finite centers, changeovers | Accepted dispatch schedule vs line throughput | Nonpreemptive finite greedy night algorithm; real setup/material constraints pending |
| 08 | Manufacturing order / Planning + Production | Draft, replan, release, quarantine/hold, restart and cancellation with permission separation and irreversible audit | Operator/planner role matrix, rollback and race tests | Draft/release snapshots, roles, controlled engineering reassignment; field approval pending |
| 09 | Semi-finished / Shop floor | 2-step semi-finished batch with splits, mixed lots, rework, scrap and partial transfer | Operator records and quantity conservation | Routed confirmations, QC-gated WIP/lot; mixed-lot corrections pending |
| 10 | Final assembly / Quality + Production | Actual model variant, exact semi-finished batch, final EOL release, serial issuance | Tested finished absorber characteristics and signoff | EOL + assembly lot evidence; variant policy and EOL plant criteria pending |
| 11 | Line-side use / Production | Consume by real confirmation lot, verify BOM cap and single auditable reversal | Approved substitution and line source evidence (internal only) | Lot-BOM bounded records and reversal; substitution governance pending |
| 12 | Deviation/CAPA / Planning + QA | Raise approved MPS deviation, assign/revise action, close, independent QA reject→reopen→revise→approve; self-approval must fail | Signed effectiveness observations and closure policy | Stage 23 independent QA evidence+reopen and Stage 24 role guards; operator/QA UAT pending |
| 13 | In-process QA / Quality | Control Plan tolerance, nonconformance containment and independent disposition; verify characteristics measured | Approved sampling, MSA, calibration and reaction plan | Inspection gates and holds; SPC baselines and physical validation pending |
| 14 | OEE / Production engineering | Cross-midnight work/stop timing, overlap, legacy duration stop and planned vs actual time | Machine clock check, actual loss taxonomy and quality/rework accuracy | Timed stop reconciliation with unverified flags; independent machine evidence pending |
| 15 | Genealogy / Quality | Serial from known good lot → prior semi-finished confirmed lot; run mock recall of intentionally rejected batch | End-to-end scan/label/recall drill and boundary completeness | Local serial + lot explorer; recall UAT and label workflow pending |
| 16 | What-if / Planning | 2 or more alternative shift/center/material hypotheses; ensure no writes to approved baseline | Approved comparison criteria and selected scenario | Capacity scenario previews; multi-factor sensitivity pending |
| 17 | Internal conversion cost / Cost controller | Release at rate A, change current rate to B, compare actual clock vs frozen historical rate A | Finance-confirmed costing policy (manual approval, no connection) | Frozen rate evidence; overhead/cost owner approval pending |
| 18 | Production dashboards / Management | Verify KPI sources/filters against raw order, quality and CAPA; role-specific read-only access | Target definitions, threshold approval, exports and signed drilldowns | KPI and alerts; signed targets/export/UAT pending |

## Execution result template (copy for each scenario)
- **Module / acceptance scenario / work center / model / lot**:
- **Git SHA / build / SQL migration version / test environment**:
- **Executor and role / second independent reviewer**:
- **Iran-local execution time and Jalali date**:
- **Actual observed result, numeric quantity and reconciliation**:
- **Evidence links, screenshots and audit identifiers**:
- **Defects and severity / linked corrective action**:
- **Retest result**: NOT RUN / PASS / FAIL / BLOCKED
- **Owner signature / QA signature / dated release decision**:

**Release rule:** mark a module complete only when its automation, permission/race tests, representative real factory UAT and owner-signed SOP/rollback are all evidenced. Source files, API availability, partial unit tests, and CI runs failing before steps are never a PASS.
