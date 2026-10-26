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
The system now includes a development-only local ASP.NET Core Identity login with role-gated API, cookie expiration, rate limiting and CSRF on mutations. No default credentials are embedded. The initial admin is created only when Bootstrap credentials are supplied and SQL schema is ready. Do not set a real password in repository files or share credentials in chat. Granular department/module permissions, security acceptance and IIS checks are still outstanding. The host deliberately refuses non-Development environments. Do not expose the developer port to untrusted networks.
Inventory management, purchases, sales, employees and **all integrations to those systems** are explicitly out of scope. Optional future user-approved read-only item master view is not connected.

## Modules and advancement
Use `docs/SCOPE.md` for all 18 modules and actual completion status. Never label gross requirements as net MRP, shift master data as CRP, or simple production plans as full MPS. Plan version approvals, labor standards, material reservation, BOM effectivity, and scheduler integration are forthcoming.

## Stage 3 planning workflow (manual assumptions only)
1. Define items and work centers at `/app.html`.
2. Define and approve routing operations and shifts.
3. In `/planning.html`, define versioned BOM for finished/semi-finished items. Approve only non-overlapping revisions.
4. Create MPS version, add finished-product lines using Jalali picker, approve to lock.
5. Enter dated baseline planning balances for **every demanded/derived item**, and any dated expected receipts **after** baseline date. All balances must share a common baseline date.
6. Run net MRP; snapshot stores input and outputs; CRP displays daily rough-cut overload only.

The MRP engine deliberately refuses to guess unknown item balances/lead-time policies, use cyclic or absent approved BOM structures for manufactured items, or double count scheduled receipts before the stock snapshot. Historical gross-BOM preview under the old `/app.html` page is separate and is not automatically included in versioned-BOM MRP. True finite-capacity scheduling, subcontracting, alternate BOM selection and comprehensive pegging remain later milestones.

## Stage 4 manufacturing execution workbench
1. Open `/login.html` to log in using the development-only administrator username/password configured through user secrets.
2. Open `/app.html` to define semi-finished or finished items, work centers, routing operations and work shifts.
3. In `/planning.html`, create and approve an effective-dated BOM revision, covering the planned order start.
4. Open `/execution.html`; create a work order specifying internal order number, item, planned quantity, start and due dates (all dates selected using shared Jalali calendar).
5. Release the draft work order. The exact approved routing operations are copied to immutable order operation records.
6. Confirm good, scrap and rework quantities by operation in sequence. A later operation cannot consume more than previous operation's good output; the sum of dispositions never exceeds input quantity. Full scrap at an upstream operation closes zero-input downstream steps.
7. Optional: place order on hold and resume, log production downtime, or inspect audit/history.
8. The final operation's good quantity is shown as the order's good output. There is no stock transaction or purchasing operation.

### Controls and known limitations
- The release action verifies the selected BOM and routing approvals and their effective validity.
- The `RowVersion` concurrency token protects order state updates during confirmation; database-level runtime concurrency has not yet been validated with two parallel operators.
- Lot labels on confirmations are basic references, not end-to-end traceability.
- Current shop-floor workflow requires full completion of a preceding operation before confirming a subsequent step. Partial transfers, combined line throughput, scrap disposition approval, NC/rework processing, line material backflush and OEE remain future features.
- Role-gated APIs do not imply granular role-based privileges for planners versus operators. Only Administrator is bootstrapped currently.
- All data lives in dedicated IPPCS SQL tables. No existing warehouse/purchasing/sales/HR APIs or SQL views are called.
