# Stage 54 — BOM alternatives and order-level material mix

## Purpose and design rules

An alternative component is a substitute for the same BOM requirement, not another additive requirement. The implementation follows controlled-BOM, effective-dating, revision integrity, material identification and traceability principles. It does not claim that a particular standard mandates this data model; plant engineering and quality owners must approve the local substitution rules.

Each versioned BOM line may be either mandatory or a member of an alternative group. Every alternative line retains its own component, quantity-per-finished-unit, standard scrap percentage and priority. Every group must contain at least two options, exactly one default, a common BOM sequence, unique priorities and no duplicate component. The database also protects unique group priorities, at-most-one default and positive planning metadata.

A BOM revision is approved as one engineering-controlled structure and remains effective only in its approved date interval. Alternatives do not create separate concurrent BOM demand.

## Planning behavior

Baseline MRP, net MRP pegging, gross-demand provenance and MRP lot-capacity preview use mandatory lines plus the one default option from each alternative group. They never add every possible substitute to the same demand. This avoids false shortage calculations and double-counted component requirements.

For an order-specific mix, the planner assigns a percentage of the planned finished-product quantity to each selected option. The percentages for each group must total exactly 100%. Each option's own quantity-per and scrap factor are applied to its share. For example, for 100 finished units, a 60% allocation to a component with 1.0 unit per finished unit creates 60 units of planned component demand; a 40% allocation to an approved substitute with 1.2 units per finished unit creates 48 substitute units.

The allocation endpoint saves every group for an order in one SQL transaction. On release, the server verifies that allocations reference only alternatives on the order's frozen BOM and that every selected group totals 100%. Groups without an explicit allocation use the single approved BOM default.

## Execution and traceability

The execution page exposes an "Alternatives and material charging" area. A planner may change an order's group mix while the order is Draft, Released or InProgress, but only before any actual material usage has been recorded for that group. Once the first actual usage is recorded, that group's mix is locked; other groups with no usage may still be adjusted. Every change is recorded in the manufacturing-order audit journal.

Shop-floor users record actual material quantity, component lot code, reason/variance note and authenticated actor against the exact BOM line frozen on the order. Cumulative consumption above the planned amount requires a note. The order page and print document show the authorized options, selected production mix, planned quantity, actual quantity, lot code and actor.

These are IPPCS manufacturing-planning and traceability records only. They do not create warehouse issues, receipts, stock balances, purchasing orders or accounting transactions. They are not a substitute for an inventory system's controlled issue/return transactions.

## Data and deployment

The additive EF migration is 20261029140000_StageFiftyFourBomAlternatives. Existing BOM lines are preserved: a null alternative group keeps the line mandatory, and the default alternative priority is 1. The SQL model snapshot and migration target model are updated. The normal build workflow generates the self-contained SQL installer from the committed EF migrations; do not hand-edit the generated installer.

## Verification and factory UAT

Added unit coverage checks:
- Baseline MRP only counts the default alternative.
- Net MRP pegging and gross demand provenance do not include unselected alternatives.
- BOM approval rejects multiple defaults.
- Mixed alternatives apply each option's own usage ratio and scrap factor.

Browser contract coverage checks BOM authoring fields, order mix allocation, actual material usage and history, and print evidence.

Before factory acceptance, execute at least these scenarios with real engineering-approved part data:
1. One mandatory component and one alternative group with two options.
2. A multi-option group with one default; attempt approval with zero and two defaults.
3. MRP with sufficient stock only for the default option, then compare the expected net shortage.
4. A 60/40 order mix where substitute quantity-per differs from the original; verify planned quantities.
5. Attempt a 99.99% or 100.01% group and verify server rejection.
6. Record the first issue/consumption against one option; verify that group can no longer be remixed.
7. Change a different unused group and verify the consumed group remains unchanged.
8. Record multiple lots, an over-plan variance with reason, and verify actor/UTC timestamp/history.
9. Print the released order and BOM; reconcile printed alternatives and actual consumption with API records.
10. Confirm no warehouse or purchasing transaction was created.

This feature is not factory-accepted until the relevant owners complete and sign the applicable UAT gates. A green CI run is necessary but not sufficient.
