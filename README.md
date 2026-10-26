# IPPCS — سامانه مستقل برنامه‌ریزی و کنترل تولید کمک‌فنر

A stand-alone production planning/control system, using ASP.NET Core and SQL Server. The entire 18-module manufacturing scope is tracked in [docs/SCOPE.md](docs/SCOPE.md).

## Strict scope boundary
No warehouse, procurement, sales, or HR modules and no operational connections with those systems. Only independent planning inputs are supported. A warehouse **read-only item catalog query** may be considered later if the owner provides it; it is **not** connected now.

## Stage 3 (development preview)
- Responsive Persian RTL [foundation dashboard](/src/IPPCS.Web/wwwroot/app.html) for reference items, work centers, original gross BOM, routing versions, weekly shifts and basic production plans.
- [Stage-three planning console](/src/IPPCS.Web/wwwroot/planning.html) for independently versioned BOM structure with dated approvals and cyclic dependency checks.
- Approved and locked MPS version, finished-product demand lines and Jalali due dates.
- Manually managed planning balances (gross, reservations, quarantine, safety) and expected supply dates; **not warehouse or purchasing transactions**.
- Net MRP **prototype** with available balance, dated expected receipts, lot minimum/multiple, lead-time offsets, BOM explosion on planned quantities, date-effective approved revisions and input/output calculation snapshots.
- Rough-cut CRP daily load-versus-weekly shift capacity preview based on approved routings; **not finite-capacity APS**.
- Shared Jalali picker from previous user projects, verified with JavaScript and server-side unit tests.
- GitHub CI builds, tests date logic and planning calculations, checks browser JavaScript and EF Core migration scaffolding.

## Important restrictions
**Not production-ready.** Missing: authentication, permission checks, reviewed/committed DB migrations, real SQL Server integration tests, comprehensive MRP pegging, true finite-capacity CRP/APS, MES/order execution and other agreed manufacturing modules. Non-Development hosting is intentionally blocked. No source data from existing systems is accessed.

## Developer setup
See [docs/DEVELOPER_GUIDE.md](docs/DEVELOPER_GUIDE.md) and [docs/JALALI_DATE_STANDARD.md](docs/JALALI_DATE_STANDARD.md).
Use a dedicated disposable dev database, configure the SQL Server connection via .NET user secrets, generate/review migrations, run `dotnet ef database update`, then run the web host in Development. Open `/app.html`, then follow the link to `/planning.html`.

Working branch: `feature/production-platform-foundation` (Draft PR #1). Do not merge to `main` before security and acceptance review.
