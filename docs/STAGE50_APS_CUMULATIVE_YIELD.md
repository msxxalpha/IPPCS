# Stage 50 — Cumulative yield in finite APS operation sizing

## Problem addressed
The APS task builder previously sized every operation using the finished-good MPS quantity divided only by that operation's own yield. That was optimistic for upstream steps: they also have to cover losses incurred by downstream operations.

## Calculation contract
For final-good demand quantity **Q**, and operation yields **yᵢ** expressed as percentages, the quantity processed by operation **i** is:

**Qᵢ = Q / Π(yⱼ / 100)** for all operations **j** from **i** through the final operation.

Operation duration remains:

**durationᵢ = setupMinutesᵢ + runMinutesPerUnitᵢ × Qᵢ**.

The builder validates a unique, positive operation sequence, a non-empty work-center reference, a positive standard run time, non-negative setup time, a valid approved routing effective for the demand date, and yield in the interval (0, 100]. It refuses invalid inputs instead of silently understating capacity.

## Example
For 100 required good finished units and a two-step routing with yields 90% and 80%:

- Upstream step quantity: **100 / (0.90 × 0.80) = 138.888…** units.
- Final assembly input: **100 / 0.80 = 125** units.

The final good result is 100 units after both yield losses. With 100% yields at every operation, each step retains the original MPS quantity. APS output now labels the value as the input quantity processed at each step.

## Implementation and safeguards
- **FactoryApsTaskExpansion** is the shared pure builder for APS operation tasks.
- The APS endpoint uses the builder and still validates the unique approved routing applicable to each MPS line's required date.
- The Persian RTL APS result table displays per-operation input quantity; the explanatory text distinguishes a finite planning preview from actual machine reservations or factory acceptance.
- Unit tests cover cumulative multi-step yields, invalid yield rejection, and unchanged all-100% yield behaviour; UI regression tests verify the result field and endpoint wiring.
- No schema migration is needed. This remains inside IPPCS production planning; it creates no warehouse, purchasing, sales, HR or calibration module and does not connect to those systems.

## Acceptance boundary
This calculation is a software scheduling estimate. Actual yield rates, timing standards, scrap/rework classification, center/tool conflicts, and the resulting schedule must still be verified with representative factory evidence before factory acceptance. An automated green run does not mean on-site acceptance.
