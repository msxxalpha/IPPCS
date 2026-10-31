# IPPCS — criteria for completing all 18 modules

**Status:** Development, not approved for factory production. A module is *complete* only when its business rules, Persian RTL pages, granular role policy, database migration, unit + disposable SQL regression tests, and representative factory UAT have all passed. A basic menu, CRUD API or preview does not constitute completion.

| No. | Module | Implemented proof of concept | Major outstanding acceptance requirements |
|---|---|---|---|
| 01 | Factory master data | Item/work center/BOM and routing revisions, effective-date approval and shifts; BOM alternative groups with one default, unique priority and controlled quantity/scrap per option | Complete equipment/tool/resource hierarchy, engineering approval ownership, robust revision changeover and representative import/UAT validation |
| 02 | Demand / forecast | Internal demand entries, Jalali monthly moving-average forecasts | Rolling historical MAE/WAPE/bias backtest implemented; seasonality, versioned accuracy thresholds, approval and import validation remain |
| 03 | S&OP | Finite horizon demand vs capacity and bottleneck snapshots | Cross-functional approval, iterative balancing scenarios and reconciliation to MPS |
| 04 | MPS | Approved version, dated lines, draft editing, demand intake | Release calendar, revision/lock governance, exception workflows and historical baseline |
| 05 | Net MRP / pegging | Multilevel net material plan, manual stock/receipts, lot sizing, MRP snapshot; baseline MRP/net pegging use one default from each alternative group; order-specific mixed alternatives and actual material trace are implemented in execution; new MRP run snapshots retain BOM line IDs and alternative group/default/priority semantics; lot-capacity preview returns the exact product-capacity rates (Routing baseline, effective rate, yield, override/fallback) for selected route revisions | Multi-order material commitment/reconciliation and workday lead-time calendar remain; the per-order mix is not yet a firm inventory reservation or warehouse issue, and the read-only net planned-lot graph remains an unconfirmed planning proposal |
| 06 | CRP | Rough-cut demand capacity; finite nonoverlapping shift allocation preview for proposed MRP finished/semi-finished lots with route release-date validity | Actual operation start/stop audit intervals and conservative active-order blocking exist; audited local machine reservation slots now implemented; still need lifecycle governance, synchronized clocks, labor/tool calendars, shifts spanning days and plant UAT remain |
| 07 | APS | Finite no-overlap engine; proposed MRP batches honor earliest release dates; MPS APS maintenance days/material preflight/Gantt; S&OP and APS scenarios now persist the exact product-capacity evidence (Routing baseline, effective rate, yield and override/fallback) used for operation durations | Hypothetical child-before-parent dependencies and recorded operation clocks exist; still requires firm material/order netting, plant-approved maintenance-aware dispatch slots, alternatives, shift splitting and plant validation |
| 08 | Manufacturing order management | Draft/released/in-process/held/completed/cancelled, route/BOM snapshots, order-specific mixed alternative allocation, controlled group lock after first actual usage, immutable material usage/lot/actor history and print evidence | Correction/reversal governance for material usage, deeper connection to completion quantities, lifecycle role separation and representative line UAT remain |
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

## Stage 13 verified additional development work (not plant acceptance)
| Module | Increment added | Still outstanding |
|---|---|---|
| 03 S&OP | Historic-vs-live in-window demand and approved MPS quantity reconciliation | Product-mix reconciliation, owners, iterative cross-functional approvals |
| 04 MPS | Exact imported-demand GUID reference audit; manual/unmatched plan quantities reported separately | Approved version lock/revision reason, release calendar, exception workflow |
| 05 MRP | Planning-source certainty, surplus and release-before-baseline risk | Firm material netting, validated receipt commitments, production workday lead times |
| 06 CRP | Ranked bottleneck centers; overload/no-shift-day counts and undefined utilization | Operator/tool resource calendars, cross-midnight shifts, actual execution constraint governance |
| 16 What-if | Capacity multiplier **plus** maintenance blackout day sensitivity and finite allocations | Alternative routing, changeover model, cost/material constrained decision optimization |

All endpoints are local IPPCS-only. No inventory, purchase, sales or employee systems connected. Full 18-module sign-off remains open.

## Stage 14 actual guarded workflows (still not full factory UAT)
| Module | Functional improvement | Production signoff remaining |
|---|---|---|
| 01 Master revisions | Serializable graph-level BOM and item-level Routing approval, overlap/cycle/duplicate validation, explicit preflight, immutable approved revision dates | Approved ECO/changeover workflow, revision ownership/signatures, instrument/resource hierarchy, plant UAT |
| 07 APS | QC-, route-step- and clock-aware read-only dispatch board with actual machine reservation evidence and no-slot warnings | Approved real dispatch/alternative routing, shift splitting, tool/labor capacity and PLC confirmation |
| 08 Work orders | Pre-release approved-revision/MPS preflight enforced in transactional release; draft reschedule/quantity edit rebinds valid revisions with immutable actor + before/after audit | Signed release, permission separation, non-draft revision/change requests, full plant exception/rework signoff |

As previously, the release gate is still Draft; no ERP module or integration is created.

## Stage 15 controlled operational progress (partial pending UAT)
| Module | Stage 15 functional proof | Still required before plant acceptance |
|---|---|---|
| 11 Consumption | Confirmed batch+frozen BOM consumption with per-component maximum; immutable correction rows; duplicate and mismatch detection; legacy evidence distinguished | Material substitutions and shop-floor authorization, controlled auto backflush, per-shift consumption, physical stock/warehouse processes intentionally excluded |
| 12 Deviations | Immutable corrective-action before/after revision journal and reason, manual escalation and linked follow-up; initial action no longer silently overwrites earlier assignment | Independent effectiveness signoff, escalation owners/SLAs, policy for corrective action evidence and plant UAT |
| 18 Dashboards | Escalated cases flagged Critical when overdue and drill down into the referenced case, even when its due date is outside ordinary alert horizon | KPI benchmark signoff, alert acknowledgement, authorized drilldown by group, operator user testing |

Strict isolation maintained: no warehouse, purchasing, sales or HR integration; no schema reset, no approval to merge main.

## Stage 16 manufacturing closure increments (factory signoff still required)
| Module | Real runtime process and tested evidence | Remaining factory acceptance |
|---|---|---|
| 09 Semi-finished stages | Internal per-lot handoff audit across adjacent operations; lot writer rejects unresolved quarantine, pending/quality-rejected quantities, failed frozen mandatory checks | Real station transfer forms, partial mixed-lot correction approval, operator validation and UAT |
| 10 Final shock absorber assembly | Each finished lot must have frozen BOM-matched documented semi-finished order origins before internal EOL signature; genealogy locked afterward | Variant/serial labeling, 100% traceability policy, engineering BOM change control and EOL UAT |
| 13 Quality | In-plant final signoff requires completed work order, inspected last operation, no open holds, complete operation-step provenance, per-lot semi-origin; local quality role/immutable audit | Approved inspection sample policy, MSA/gauge calibration, reaction plans, independent review and client-specific EOL test requirements |

**Status:** Stage 16 CI can establish database and code regression only. It does not complete a plant-specific UAT or authorize production rollout; all 18 module release gates remain pending.

## Stage 17 planning-controlled improvements (not the 18-module release signoff)
| Module | Governed, testable Stage 17 implementation | Still needed for plant acceptance |
|---|---|---|
| 04 MPS | Immutable approved source; single pending controlled clone with mandatory reason; serialized all MPS writers; approved line/actor audit; automatically signed approval material/capacity differences | Factory-approved program change authority, official freeze horizon, owner signatures and signed UAT |
| 05 MRP | Current net material impact for controlled MPS source/candidate; result frozen with approval decision and explicit snapshot constraints | Confirmed physical material planning policy (without integration), validated supplier lead assumptions and workday calendar |
| 06 CRP | Center/day load and overload deltas from controlled MPS version, recalculated/signed at new version approval | Tool/labor/maintenance calendars, true alternative resources, plant constraint simulation and acceptance |

All 18 modules continue to require line-specific scenario approval and staged deployment validation. `docs/18_MODULE_RELEASE_GATES.md` remains the definitive factory signoff checklist.

## Stage 18 stronger code proof (NOT factory acceptance)
| Module | Integrated Stage 18 increment | Remaining launch gate |
|---|---|---|
| 01 BOM/Routing | Engineering diff against selected approved baseline; changes to parts, ratios, stations and time; report impacts on frozen active orders and overlap blockers | Formal ECO owner/signatures, dated cutover or supersession, rollback, revised Control Plan authorization and factory UAT |
| 15 Traceability | Globally unique factory product serial bound to finished actual confirmed output **after EOL QA**, immutable journal, bound per-lot quantities, serial-to-batch multi-order ancestors | Approved serial coding standard, verified label/scanning, unit split/merge trace, sampling/recall exercise, customer requirement alignment |

Nothing connects to warehouse, purchasing, sales or HR. Plant decision makers have not yet signed the full set of 18 module acceptance gates.

## Stage 19 — additional implementation, release not yet proven
| Module | Stage 19 additions | Open gate |
|---|---|---|
| 14 OEE | Start/end timed center stoppages, operation clock interlock, conservative daily reconciliation across Tehran midnight, distinguish unlocated legacy durations | Representative multi-shift UAT, reason-coded approved downtime taxonomy, known machine data, proof of source clock accuracy; exact-head CI has yet to execute |
| 17 Cost conversion | Frozen operation standard rate and explicit missing-rate evidence at work-order release, protected from later center-rate updates, separate historical audit endpoint and UI | Factory finance-approved rates and historical changes, overhead allocation/revaluation policy, cost-vs-output signoff and operational UAT; exact-head CI has yet to execute |

Still strictly no external warehouse, purchasing, sales or HR connections or modules. A failed-to-start CI is not equivalent to a validated failed compilation; both need to be distinguished.

## Stage 20 implementation — night-shift calendar (tests committed; CI not yet verified)
| Module | Functional scope advanced | Outstanding factory gate |
|---|---|---|
| 03 S&OP | True local calendar-day center capacity including previous-night minutes | Planning owner approval of actual shift coverage; scenario signoff |
| 06 CRP | Correct next-day night availability and weekend rollover; missing-day capability remains visible | Tool, labor, break and maintenance calendars with verified plant time sheet |
| 07 APS | Nonpreemptive next-day finish, two-day center occupancy, night blackout and chain precedence; versioned algorithm | Resource/tool alternatives, job split policies, validated overnight scheduling UAT |
| 08 Capacity reservation | Append-only overnight work-order reservation and accurate next-day Jalali finish; real weekly overlap prevention | Multi-operator concurrency, approved dispatch changes/rollback and plant line trial |

These rows are implementation evidence, **not an attestation of successfully run CI**. GitHub Actions infrastructure has been failing before any logged test step since Stage19. Do not merge to main or assert all 18 modules complete.


## Stage 21–22: engineering revision transition (implementation evidence, not accepted)
- Module 01 now has auditable and future-effective Routing/BOM cutover preview/commit, a distinct additive SQL engineering journal migration and nonoverlap/cycle constraints.
- Module 08 has a bounded (1–100), all-or-nothing, preview-first transfer of explicitly selected draft manufacturing orders to a valid newly approved engineering revision; frozen released orders remain on their prior historic version.
- Module 04 / 05 write operations and BOM/Routing authoring require the explicit `IpPlanningWrite` policy instead of broad production-user authentication. This is not a multi-person ECO signature.
- `/engineering-rebind.html` offers Persian RTL cutover and draft-order handling with a Jalali date component. `FactoryStageTwentyOneDraftRebindTests`, `FactoryStageTwentyTwoCutoverTests`, and disposable SQL `stage21_engineering_rebind_checks.py` / `stage22_engineering_cutover_checks.py` are linked to CI.
- **Important:** the Stage 22 engineering journal has an additive reviewed EF migration file plus an updated model snapshot, but CI has not yet verified model diff, build, unit tests or SQL migration success. GitHub Actions worker jobs for stages 19–22 fail before any actual steps or logs; do **not** report this feature as green, complete or production-ready.
- Still outstanding: reviewed ECO signatories (two-person approval), shop-floor cutover authorization, verified BOM/route precedence for older frozen orders, scheduled plan reconciliation, permission matrix audit, audit-event retention, representative factory UAT, and actual backup/rollback rehearsal. No warehouse/purchasing/sales/HR integration was added.


## Stage 23–24 — separation of duties and evidence-based effectiveness
- Module 12: closed CAPA remains **pending effectiveness** until a different authorized QA identity approves with traceable evidence. QA rejection reopens the same deviation, preserving history. The next independent approval requires a governed action revision after rejection and a new closure. Closed unverified cases remain available via `/production-deviations/effectiveness-pending`; a simple closure never earns the `Verified` status.
- Modules 08/09/13: order create/release/cancel/resume are planner/admin actions; shop-floor quantity and downtime writes are operator/admin; hold reporting permits the proper floor/quality roles; release of quality holds remains quality/admin. Authorization tests were extended in the disposable SQL chain.
- Added `FactoryStageTwentyThreeEffectivenessTests.cs`, `stage23_deviation_effectiveness_checks.py`, additional role assertions in `production_e2e_smoke.py`, and Persian review controls. Additive schema migration for Stage 23/24 is **not** required; the existing event journal is used.
- Full factory UAT cases and individual release evidence requirements are in `docs/FACTORY_UAT_RELEASE_CHECKLIST.md`. No factory owner signoff or exact-head successful CI is available for Stages 19–24. Do not merge/deploy.
