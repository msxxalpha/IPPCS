# IPPCS — سامانه یکپارچه برنامه‌ریزی و کنترل تولید

Specialized planning/control system for manufacturing automotive shock absorbers.

## Scope
All 18 agreed manufacturing modules remain in scope: [implementation register](docs/SCOPE.md).
Warehouse, procurement, sales, HR modules **and integrations with those systems** are expressly excluded.
Optional user-approved read-only item catalog view is not yet connected.

## Delivered in foundation branch
- Browser-based responsive Persian RTL shell at `/app.html`
- Manufacturing item references, work centers and multilevel BOM
- Routing revisions with start/end Jalali effectivity, draft operations and controlled approval
- Work-center weekly shifts (no overnight shifts yet)
- Simple production-plan intake using Jalali datepicker
- Multilevel **gross** material requirement preview (not net MRP)
- Shared Persian date component adapted from two existing projects, client/server validation and tests
- GitHub CI: .NET build and calendar tests, JavaScript syntax and date-roundtrip tests

**Maturity: early prototype, not production-ready.** No user login, permissions, checked-in EF migrations, complete MRP, CRP, APS, production execution or live operational connectivity. To prevent unsafe accidental deployment, the server refuses environments other than `Development`.

## Developer instructions
See [setup and safety notes](docs/DEVELOPER_GUIDE.md) and [shared Jalali calendar contract](docs/JALALI_DATE_STANDARD.md).
```sh
cd src/IPPCS.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Production" "<development SQL Server connection string>"
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialProductionFoundation
dotnet ef database update
dotnet run --environment Development
```
Review generated migrations prior to execution; use only a disposable development database.

## Working branch
`feature/production-platform-foundation`, Draft PR #1, `main` left unchanged.
