# IPPCS developer setup

## Working branch
`feature/production-platform-foundation` (Draft PR #1); `main` remains unchanged until review.

## SDK and database
Use .NET 10 SDK and a dedicated development-only SQL Server database. A SQL Server connection string is not stored in Git:
```sh
cd src/IPPCS.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Production" "Server=localhost;Database=IPPCS_Dev;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialProductionFoundation
dotnet ef database update
dotnet run --environment Development
```
Do not run `EnsureCreated` against a persistent DB. Generate, inspect, then commit reviewed EF migrations before non-development deployment.
Database schema has not been deployed or integration-tested as part of the bootstrap commits.

## Browser
Visit the development URL root. It redirects to `/app.html`. The responsive RTL UI supports item catalog, work centers, multilevel BOM, routing versions, routing operations, work shifts, and simple prototype production plans.

## Prototype restrictions
The API does **not** currently have authentication. The server intentionally rejects non-Development environments. Do not expose the developer port to untrusted networks.
Inventory management, purchases, sales, employees and **all integrations to those systems** are explicitly out of scope. Optional future user-approved read-only item master view is not connected.

## Modules and advancement
Use `docs/SCOPE.md` for all 18 modules and actual completion status. Never label gross requirements as net MRP, shift master data as CRP, or simple production plans as full MPS. Plan version approvals, labor standards, material reservation, BOM effectivity, and scheduler integration are forthcoming.
