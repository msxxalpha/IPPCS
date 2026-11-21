const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const read=name=>fs.readFileSync(path.resolve(__dirname,"../src/IPPCS.Web/wwwroot",name),"utf8");
const page=read("factory-master-data.html");
const home=read("app.html");
const deviations=read("production-deviations.html");
const script=read("js/factory-master-data.js");
const choices=read("js/factory-choices.js");
const client=read("js/production-deviations.js");

test("factory basic-data master manager is discoverable with accessible Persian RTL forms",()=>{
 assert.match(page,/<html lang="fa" dir="rtl">/);
 assert.match(page,/id="master-kind"/);
 assert.match(page,/id="master-query"/);
 assert.match(page,/id="master-list"/);
 assert.match(home,/href="\/factory-master-data.html"/);
 assert.match(deviations,/href="\/factory-master-data.html"/);
 for(const id of ["action-owner","cause-group","revise-owner","revise-cause-group",
                   "deviation-page-size","deviation-search"]){
  assert.match(deviations,new RegExp('id="'+id+'"'));
 }
});
test("catalog writes are authenticated, safe-DOM only and disable mutation for non-admin",()=>{
 assert.match(script,/IPPCSSecurity\.secureFetch/);
 assert.match(script,/includes\("Administrator"\)/);
 assert.match(script,/isActive/);
 assert.match(script,/textContent/);
 assert.doesNotMatch(script,/\.innerHTML\s*=|insertAdjacentHTML/);
});
test("shared searchable select keeps native semantics and does not select an unrelated filtered value",()=>{
 assert.match(choices,/document\.createElement\("option"\)/);
 assert.match(choices,/select\.value=""/);
 assert.match(choices,/field\.type="search"/);
 assert.match(choices,/ArrowDown/);
 assert.doesNotMatch(choices,/\.innerHTML\s*=|insertAdjacentHTML/);
});
test("CAPA uses stable master codes, server paging and MPS-peg filtering rather than same-item guessing",()=>{
 assert.match(client,/rootCauseCategoryCode/);
 assert.match(client,/responsibleCode/);
 assert.match(client,/production-deviations\/search\?/);
 assert.match(client,/production-pegging\/mps\//);
 assert.doesNotMatch(client,/\.innerHTML\s*=|insertAdjacentHTML/);
});

test("older shop-floor forms use reportable master lists and shared searchable native choices",()=>{
 const execution=read("execution.html");
 const q=read("js/quality-oee.js"),e=read("js/execution.js");
 for(const id of ["stop-reason-code","timed-stop-reason-code","quality-reason-code"]){
  assert.match(execution,new RegExp('id="'+id+'"'));
 }
 assert.match(execution,/src="\/js\/factory-choices\.js"/);
 assert.match(e,/reasonCode/);
 assert.match(e,/factory-master-options\?kind=DowntimeReason/);
 assert.match(q,/factory-master-options\?kind=QualityHoldReason/);
 assert.match(q,/reasonCode/);
 for(const file of ["control-plan.html","planning.html","planning-advanced.html",
                    "mps-pegging.html"]){
  assert.match(read(file),/src="\/js\/factory-choices\.js"/,file);
 }
 for(const file of ["js/control-plan.js","js/planning-stage3.js",
                    "js/planning-advanced.js","js/mps-pegging.js"]){
  assert.match(read(file),/IPPCSChoices\.fill/,file);
 }
});
test("master-data reporting uses Jalali date controls and safe tabular rendering",()=>{
 assert.match(page,/id="master-reason-report"/);
 assert.match(page,/data-jalali-date/);
 assert.match(script,/production-reason-summary/);
 assert.doesNotMatch(script,/\.innerHTML\s*=|insertAdjacentHTML/);
});
