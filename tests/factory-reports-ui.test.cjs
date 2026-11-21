const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web");
const read=file=>fs.readFileSync(path.join(root,file),"utf8");
const page=read("wwwroot/factory-reports.html");
const js=read("wwwroot/js/factory-reports.js");
const api=read("FactoryManagementReports.cs");
const menu=read("wwwroot/app.html");
const security=read("wwwroot/js/security-client.js");

test("report hub is accessible Persian RTL, has real Jalali pickers, searchable filters and safe navigation",()=>{
 assert.match(page,/<html lang="fa" dir="rtl">/);
 for(const id of ["report-from","report-through","report-item","report-center",
  "report-run","rpt-orders","rpt-centers","rpt-daily","rpt-capa",
  "kpi-orders","q-quantity","report-definitions"]){
  assert.ok(page.includes('id="'+id+'"'),id);
 }
 assert.equal((page.match(/data-jalali-date/g)||[]).length,2);
 assert.match(page,/factory-choices\.js/);
 assert.match(page,/factory-reports\.css/);
 assert.match(menu,/href="\/factory-reports.html"/);
 assert.match(security,/factory-reports\.html/);
});

test("report metrics are server-side, date bounded and authenticated, no warehouse dependency",()=>{
 assert.match(api,/RequireAuthorization\("IpProductionUser"\)/);
 assert.match(api,/through\.DayNumber - from\.DayNumber > 365/);
 assert.match(api,/ConvertTimeToUtc/);
 assert.match(api,/AsNoTracking/);
 assert.match(api,/CountAsync\(ct\) > 20000/);
 assert.match(api,/ManufacturingConfirmations/);
 assert.match(api,/ManufacturingQualityHolds/);
 assert.match(api,/ProductionDeviations/);
 assert.doesNotMatch(api,/\/api\/v1\/(warehouse|sales|purchasing|hr)/i);
 assert.match(api,/MapGet\("\/dimensions"/);
 assert.match(api,/items\.Count > 1500 \|\| centers\.Count > 1500/);
 assert.match(js,/factory-reports\/dimensions/);
 assert.match(api,/overdueOpenNow/);
 assert.match(api,/missingCompletionTimestamp/);
});

test("CSV export protects formulas, includes UTF8 BOM and no unsanitized HTML",()=>{
 assert.match(js,/function safeCsv/);
 assert.match(js,/FEFF/);
 assert.match(js,/replace\(\/"/);
 assert.match(js,/textContent=/);
 assert.doesNotMatch(js,/\.innerHTML\s*=|insertAdjacentHTML/);
 assert.match(js,/IPPCSSecurity\.secureFetch/);
 assert.match(js,/new URLSearchParams/);
 assert.match(js,/dataset=null/);
 assert.match(js,/button\.disabled=false/);
});
