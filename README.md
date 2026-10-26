# IPPCS — سامانه مستقل برنامه‌ریزی و کنترل تولید کمک‌فنر

An independent manufacturing-specific planning and execution system built with .NET 10, SQL Server and Persian RTL user interfaces.

## Approved product contract
All **18 original manufacturing modules** remain in scope, tracked in [the module delivery ledger](docs/SCOPE.md).
Warehouse, purchasing, sales and HR modules are **excluded**; no live integrations with them exist.
Manual planning balances and expected receipts are IPPCS-only assumptions, not warehouse or purchasing transactions.

## Delivered development prototypes
- `/app.html`: items, work centers, simple BOM, routing revisions, shifts, gross requirements
- `/planning.html`: versioned BOM with Jalali effectivity, approved MPS, independent planning assumptions, net MRP prototype, rough-cut CRP
- `/execution.html`: semi-finished and final-assembly manufacturing orders, release and controlled lifecycle, operation confirmations, good/scrap/rework, recorded downtime and audit trail
- `/login.html`: independent local ASP.NET Identity login, account lockout, cookie expiration, CSRF validation and login rate limits
- Shared Jalali picker adapted from previous successful user projects. Server uses `PersianCalendar`.
- xUnit/Node automated checks and GitHub build + EF Core schema scaffolding.

## Implementation and security status
**Development preview only, not production-ready.**
- The stage-four shop-floor model tracks quantities and basic lot labels, but does not move physical stock, perform complete genealogy or calculate OEE.
- CRP is a rough-cut capacity preview, not finite resource scheduling.
- APS, advanced material pegging and several other promised modules have not been developed yet.
- Local authentication exists; granular per-module permissions, plant user management and security acceptance tests are still pending.
- Complete reviewed SQL migrations and practical SQL Server/IIS testing remain deployment prerequisites. Non-Development web hosting is blocked by design.
- Authenticated production APIs require CSRF request token for state changes.

## Local developer setup
Use the [full developer guide](docs/DEVELOPER_GUIDE.md). Never put SQL passwords or bootstrap passwords in committed files. Configure user secrets and a disposable development database, apply a reviewed initial schema migration, then run `IPPCS.sln` with the **IPPCS.Web (HTTPS)** profile. Default landing is `/app.html`.

## Change control
Branch `feature/production-platform-foundation`, Draft PR #1. `main` is not modified until acceptance.
