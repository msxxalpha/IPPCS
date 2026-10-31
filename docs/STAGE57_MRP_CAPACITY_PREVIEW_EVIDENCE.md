# Stage 57 — MRP lot preview capacity provenance

## Purpose

The read-only, lot-level MRP capacity preview already uses the active per-product/per-routing-operation capacity overrides when it builds finite APS tasks. This increment makes those exact assumptions visible in the preview response instead of returning only the scheduled duration.

## Behavior

Before the disposable Routing objects are adjusted for calculation, the endpoint captures WorkCenterProductCapacityEvidence from the same approved Routing revisions and active product-capacity rates used by CRP and APS. After the preview is evaluated, it filters the evidence to the Routing revisions actually selected by proposed lots and includes that evidence in the response.

For each relevant operation the evidence distinguishes:
- approved Routing run minutes per unit;
- effective run minutes per unit used by the preview;
- active product-specific units/hour, where configured;
- explicit override versus Routing fallback;
- yield, setup time, work center, operation sequence and effective Routing revision dates.

The calculation remains read-only. It does not create firm orders, reserve inventory, issue material, create warehouse movements or convert suggested lots to released production. Product-capacity override application and cumulative downstream yield sizing are unchanged.

The response algorithm identifier advances to MRP-CRP-PREVIEW-5 to distinguish the evidence-bearing response contract.

## Regression coverage

The Stage53 product-capacity UI/API contract test now requires the preview to build the evidence before applying rates, return only used Routing-revision evidence, and expose the updated algorithm identifier. Existing assertions still cover cumulative yield, task quantity, and the approved product-capacity table/migration.

## Release boundary

No database migration is required. Existing saved MRP runs are not rewritten. New preview responses carry the extra evidence. Factory validation, material commitment, maintenance-approved dispatch slots and the 18 on-site module sign-offs remain separate release gates.
