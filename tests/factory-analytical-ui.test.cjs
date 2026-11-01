const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
function read(file){return fs.readFileSync(path.resolve(__dirname,
 "../src/IPPCS.Web/wwwroot",file),"utf8");}
const html=read("factory-analytical-verification.html");
const js=read("js/factory-analytical-verification.js");
const app=read("app.html");
const planning=read("js/planning-stage3.js");
const advanced=read("js/planning-advanced.js");
const execution=read("js/execution.js");
const security=read("js/security-client.js");
test("eight analytical modules have one accessible standalone RTL guided trial page",()=>{
 assert.match(html,/<html lang="fa" dir="rtl">/);
 for(const id of ["analytics-item","analytics-mps","analytics-order",
  "analytics-closed","analytics-date","analytics-horizon","analytics-factor",
  "analytics-cards","analytics-refresh"]){
  assert.match(html,new RegExp('id="'+id+'"'));
 }
 assert.match(html,/data-jalali-date/);
 assert.match(html,/factory-choices\.js/);
 assert.match(html,/role="status"/);
 assert.match(app,/href="\/factory-analytical-verification.html"/);
});
test("all eight tests invoke real scoped production APIs and disclose missing evidence",()=>{
 for(const route of [
  "/factory-demand/backtest/","/factory-sop","/mrp/runs/",
  "/crp/","/factory-aps/runs/","/oee/clock-verified",
  "/factory-whatif/capacity","/factory-cost/orders/"]){
  assert.ok(js.includes(route),route);
 }
 assert.match(js,/module\.checks/);
 assert.match(js,/module\.canRun/);
 assert.match(js,/releaseAuthorized!==false/);
 assert.match(js,/factoryAccepted!==0/);
 assert.match(js,/IPPCSSecurity\.secureFetch/);
 assert.doesNotMatch(js,/\.innerHTML\s*=|insertAdjacentHTML/);
});
test("historical planning and OEE pages open requested working tabs and login returns to verification",()=>{
 for(const code of [planning,advanced,execution]){
  assert.match(code,/location\.hash\.slice\(1\)/);
  assert.match(code,/\.click\(\)/);
 }
 assert.match(security,/factory-analytical-verification\.html/);
});
test("scenario capacity comparison uses explicitly bounded inputs, not write to MPS or actual orders",()=>{
 assert.match(js,/horizon<1\|\|horizon>90/);
 assert.match(js,/factor<=0\|\|factor>2/);
 assert.match(js,/baselineCapacity:1,scenarioCapacity:factor/);
 assert.doesNotMatch(js,/\/mps\/.*\/approve/);
 assert.doesNotMatch(js,/\/manufacturing-orders\/.*\/release/);
});
