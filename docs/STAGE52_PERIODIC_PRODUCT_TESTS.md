# Stage 52 — Periodic and annual product/process tests

## Scope
This is a quality-verification workspace within the production system. It creates recurring due dates for product or semi-finished-product tests, records sample references and test outcomes, and preserves completed evidence. It is not a calibration module, maintenance module, test-instrument register, or interface to another enterprise system. Calibration continues in the organization's separate system.

## Test plan
A plan records a unique code, product, test name, cadence, interval in Jalali-calendar months, first due date, optional validity end, method reference, acceptance criteria, optional numeric limits/unit, per-run sample count, creator and creation timestamp. The plan is not editable after creation; a change to method or acceptance criteria requires a new plan code. Plans can be deactivated while prior schedules and results are retained.

Annual cadence requires a 12-month interval. Periodic cadence requires 1–11 months. Numeric lower/upper limits must be ordered, and a numeric limit requires an active internal unit-of-measure option. Plans apply only to IPPCS finished goods or semi-finished items.

## Scheduling
The user selects a Jalali date range (maximum ten years) and generates due occurrences for active plans. Each occurrence is unique by (plan, due date), uses the original first-due date as the recurrence anchor to avoid month-end drift, and is generated idempotently. Schedule generation is serialized with a SQL application lock and a unique database index. The operation has caps on plans, occurrences and displayed rows instead of silently truncating or creating an unbounded result set.

## Recording evidence
An occurrence can be completed only once through the UI/API. The user records the actual Jalali performance date, sample/batch reference, optional measured value, result outcome, report reference and explanatory notes. A value outside configured limits cannot be marked as passed or inconclusive. A numeric test cannot pass without a numeric value, and failed/inconclusive outcomes require an auditable explanation. Recorded results have actor and UTC timestamp. This workspace does not alter control-plan inspection evidence, manufacturing-order release gates, or automatically authorize product release.

## Validation
The feature includes:
- pure date recurrence tests for annual intervals, periodic intervals, month-end clamping, validity boundaries and inactive plans;
- result validation tests for numeric limits and qualitative tests;
- UI contract tests for the RTL page, menu grouping, Jalali fields, API wiring and result safeguards;
- SQL Server migration/schema and EF snapshot checks in consolidated CI.

A green CI result validates software contracts and disposable SQL execution only. It does not establish physical laboratory/line acceptance, approved test methods, actual sampling policy, measurement-system verification, or instrument calibration. These remain subject to the factory's quality procedures.
