const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web");
const read=x=>fs.readFileSync(path.join(root,x),"utf8");
test("shared tool groups are selectable reference data, never free-text per work center",()=>{
 const html=read("wwwroot/planning-advanced.html");
 const js=read("wwwroot/js/planning-advanced.js");
 const master=read("wwwroot/factory-master-data.html");
 const server=read("FactoryMasterData.cs");
 for(const id of ["aps-tool-center","aps-tool-code","aps-add-tool","aps-tool-list"])
  assert.ok(html.includes('id="'+id+'"'),id);
 assert.match(js,/factory-master-options\?kind=SharedToolGroup/);
 assert.match(js,/sharedTools/);
 assert.match(js,/toolGroupCode/);
 assert.match(master,/value="SharedToolGroup"/);
 assert.match(server,/public const string SharedToolGroup/);
 assert.doesNotMatch(js,/\.innerHTML\s*=|insertAdjacentHTML/);
});
test("finite APS actually reserves shared tooling while respecting existing clocks",()=>{
 const api=read("FactoryDemandSopAps.cs");
 assert.match(api,/sharedToolGroups:sharedTools/);
 assert.match(api,/toolBusy/);
 assert.match(api,/groupWindows/);
 assert.match(api,/sharedToolInputs\.Count>100/);
 assert.match(api,/FactoryMasterData\.Active\(db,/);
 assert.match(api,/sharedToolAssignments=sharedTools\.Count/);
});
