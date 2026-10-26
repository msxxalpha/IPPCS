# IPPCS — Integrated Production Planning & Control System

Independent manufacturing production planning/control for shock absorber factory.

## Strict scope
All 18 production modules are tracked in [docs/SCOPE.md](docs/SCOPE.md).
No warehouse, purchasing, sales or HR modules, and **no integrations** to these systems.
A user-supplied read-only SQL view for an item catalog may be considered later, only when authorized.

## Current state
**Foundation prototype only: not production-ready.**
Initial API: items, work centers, BOM with basic cycle detection, simple production plans, multilevel gross material requirements.
Not yet built: net MRP, finite CRP, APS, shop-floor execution, approvals, Persian UI, access roles, migrations, or tests.

## Getting started
Requirements: .NET 10 SDK and SQL Server.
```sh
cd src/IPPCS.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Production" "<SQL Server connection string>"
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialFoundation
dotnet ef database update
dotnet run
```
The SQL schema migration must be inspected before applying to any non-development database.
Swagger at `/swagger` in Development.

## Delivery control
Work is isolated on `feature/production-platform-foundation` until reviewed.
Build workflow: `.github/workflows/build.yml`.
