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
