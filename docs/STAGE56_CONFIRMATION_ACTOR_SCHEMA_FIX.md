# Stage 56 — repair the manufacturing confirmation actor schema

## Defect confirmed by the exact-head SQL E2E log

The ManufacturingConfirmation entity and the committed EF model snapshot contain the required RecordedBy actor column. The original CI base-table migration 20261026184038_CiSchemaSmoke, however, created ManufacturingConfirmations without that column. This escaped the pending-model-change check because the current model and latest snapshot already agreed.

The read-only factory priority queue loads recorded final-operation confirmations. On a freshly migrated SQL Server, EF selected the model's RecordedBy column and SQL Server returned Invalid column name 'RecordedBy'. This made the queue return HTTP 500 before the downstream authenticated manufacturing E2E could run.

## Correction

A new forward-only migration, 20261031110000_StageFiftySixManufacturingConfirmationRecordedBy, adds RecordedBy nvarchar(256) NOT NULL to ManufacturingConfirmations with the compatibility default unknown. Existing confirmation rows are backfilled by SQL Server's default behavior. New operation confirmations continue to store the authenticated actor.

The migration has a target-model designer derived from the current committed model snapshot. No old migration was edited, no existing record is deleted or rewritten manually, no new EF entity/table is introduced, and no live/frozen UAT database is touched.

## Verification gate

The exact-head consolidated workflow must pass:
1. Release build and xUnit/browser/RTL/Jalali tests;
2. EF snapshot consistency;
3. all versioned EF migrations on a disposable SQL Server;
4. complete one-file installer on a second fresh database, including idempotent replay;
5. authenticated API smoke, including factory release-priority queue; and
6. full authenticated manufacturing SQL E2E and the read-only first-UAT access gate.

This migration fixes a confirmed database-schema omission. It does not change the factory acceptance result: 0/18 on-site operator approvals and no production-release authorization.
