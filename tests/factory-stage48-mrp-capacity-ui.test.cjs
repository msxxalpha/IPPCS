const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const html=fs.readFileSync(path.join(root,"planning-advanced.html"),"utf8");
const js=fs.readFileSync(path.join(root,"js/planning-advanced.js"),"utf8");

test("net MRP lot capacity preview is reachable from the planning workspace",()=>{
 for(const id of ["mrp-capacity-preview-form","mrp-capacity-summary",
  "mrp-capacity-lots","mrp-capacity-allocations","mrp-capacity-unscheduled"])
  assert.match(html,new RegExp('id="'+id+'"'),"missing MRP capacity UI element #"+id);
 assert.match(html,/data-mps required/);
 assert.match(html,/name="horizon" type="number" min="1" max="90"/);
 assert.match(html,/name="capacity" type="number" min="0.1" max="2"/);
 assert.match(js,/addForm\("#mrp-capacity-preview-form"/);
 assert.match(js,/\/mrp\/".*lot-capacity-preview/);
});

test("MRP capacity preview displays lot-level status, dates, allocations and unscheduled reasons",()=>{
 for(const text of ["r.onTimeLots","r.lateLots","r.blockedLots","r.activeCapacityReservations",
  "r.runningOperationClockSessions","r.lots","r.allocations","r.unscheduled",
  "proposedReleaseJalali","receiptDueJalali","operationsScheduled","r.disclaimer"])
  assert.ok(js.includes(text),"missing preview result binding: "+text);
 assert.match(js,/در انتظار/);
 assert.match(js,/زمان‌بندی‌نشده/);
});

test("MRP capacity preview stays read-only and does not create firm production orders",()=>{
 const form=html.slice(html.indexOf('id="mrp-capacity-preview-form"'),
  html.indexOf('id="mrp-capacity-summary"'));
 assert.match(form,/پیش‌نمایش/);
 assert.match(form,/دستور ساخت یا رزرو واقعی ایجاد نمی‌کند/);
 assert.doesNotMatch(js,/api\("\/manufacturing-orders".*POST/);
});
