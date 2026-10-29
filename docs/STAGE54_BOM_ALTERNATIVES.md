# Stage 54 — BOM alternatives and order-specific material allocation

## Scope and design
This change is confined to IPPCS production planning and execution. It does not connect to warehouse, purchasing, sales, HR, or external inventory services.

- A versioned BOM line with an empty `AlternativeGroupCode` is mandatory.
- Two or more lines with the same non-empty group code are mutually substitutable options for one requirement. They share one operation sequence, have distinct priorities, and exactly one option is the default.
- `QuantityPer` and `ScrapPercent` remain option-specific. For a group, a 100% allocation to an option means its standard quantity for the whole order. A mixed allocation applies each option's percentage to its own standard quantity.
- Approval rejects incomplete groups, zero/multiple defaults, duplicate priorities, duplicate components, and conflicting sequence assignments.
- Baseline MRP, gross provenance, and net pegging use mandatory lines plus exactly one default option per group. They never add every alternative as separate gross demand.
- A draft, released, or in-progress manufacturing order can have a group-specific mix saved while no actual material use has been recorded for that group. Percentages must total exactly 100%. Once the first actual use is recorded for any member of that group, the mix is locked.
- Actual material use records the frozen BOM line, component identity, quantity, optional lot code, timestamp, authenticated actor, and note. A read-only history endpoint and UI show cumulative quantities and individual usage events. Consumption above planned quantity requires a note. Audit events record allocation and usage.
- These records are internal production evidence only. They are not a warehouse issue, receipt, stock adjustment, or purchasing transaction.

## Relevant endpoints
- `POST /api/v1/bom-revisions/{id}/lines` — add a mandatory line or an alternative option.
- `POST /api/v1/bom-revisions/{id}/approve` — approve only after alternative-group validation.
- `GET /api/v1/manufacturing-orders/{id}/material-options` — view order-level option shares, planned quantities, and actual usage.
- `POST /api/v1/manufacturing-orders/{id}/material-options/{groupCode}` — allocate a group across authorized options, total 100%.
- `POST /api/v1/manufacturing-orders/{id}/material-usage` — append actual component use and lot evidence.
- `GET /api/v1/manufacturing-orders/{id}/material-usage` — read append-only usage history including actor and timestamp.
- Order release validates persisted allocations before creating the released order evidence.

## SQL and tests
Migration `20261029140000_StageFiftyFourBomAlternatives` adds the optional group/default/priority metadata and the order allocation/usage tables. Existing BOM lines remain mandatory by default through migration defaults.

Unit tests cover default-only MRP demand and rejection of multiple defaults. Browser contract tests cover BOM entry, CSV import, order-level allocations, and actual usage UI. The consolidated GitHub Actions run remains unverified while the repository's GitHub Actions billing/spending-limit lock is unresolved; run the full .NET, JavaScript, EF model-drift, migration, and disposable SQL checks at the latest branch head before merge or deployment.
