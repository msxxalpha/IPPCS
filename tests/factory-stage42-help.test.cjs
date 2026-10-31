const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const vm=require("node:vm");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const read=name=>fs.readFileSync(path.join(root,name),"utf8");
const context={window:{}};
vm.runInNewContext(read("js/help-catalog.js"),context);
vm.runInNewContext(read("js/help-fields.js"),context);
const guide=context.window.IPPCSHelpCatalog;
const fieldMap=context.window.IPPCSHelpFields;
test("all existing application forms have purpose and instructions, not only generic page descriptions",()=>{
 assert.equal(guide.pages.length,15);
 let count=0;
 for(const page of guide.pages){
  const markup=read(page.page.slice(1));
  const formIds=[...markup.matchAll(/<form\b[^>]*\bid="([^"]+)"/g)]
    .map(m=>m[1]).sort();
  const documented=Array.from(page.forms,row=>row[0]).sort();
  assert.deepEqual(documented,formIds,"Incomplete guide: "+page.page);
  assert.ok(page.purpose.length>20&&page.before.length>15,page.page);
  for(const [id,title,usage] of page.forms){
   assert.ok(title.length>=4 && usage.length>=25,id);
   count++;
  }
  assert.ok(page.workflows.length>0,page.page);
 }
 assert.equal(count,67);
});
test("deployment sequence names concrete prerequisites and validation gates",()=>{
 assert.equal(guide.deployment.length,14);
 for(const step of guide.deployment){
  assert.ok(step.title&&step.data&&step.action&&step.done);
  assert.ok(step.data.length>20&&step.action.length>30);
  assert.ok(guide.pages.some(p=>p.page===step.page),step.page);
 }
 assert.match(guide.scope,/کالیبراسیون/);
 assert.match(guide.scope,/انبار/);
});
test("manual navigation, contextual form links and deep links are present without unsafe HTML",()=>{
 const html=read("help.html"),js=read("js/help.js");
 const security=read("js/security-client.js");
 for(const id of ["help-rollout","help-forms","help-search",
  "help-page-filter","help-show-forms","help-show-rollout"])
   assert.ok(html.includes('id="'+id+'"'),id);
 assert.match(js,/URLSearchParams/);
 assert.match(js,/aria-pressed/);
 assert.match(js,/textContent/);
 assert.doesNotMatch(js,/\.innerHTML\s*=|insertAdjacentHTML/);
 assert.ok(html.includes("help-fields.js"));
 assert.match(js,/IPPCSHelpFields/);
 assert.match(security,/data-ip-form-help/);
 assert.match(security,/\/help\.html/);
 assert.match(read("login.html"),/\/help\.html/);
});

test("field-level help labels cover every real form",()=>{
 let count=0;
 for(const page of guide.pages){
  for(const [id] of page.forms){
   assert.ok(Array.isArray(fieldMap[page.page]?.[id]),"Missing "+page.page+" "+id);
   assert.ok(fieldMap[page.page][id].length>0,id);
   count++;
  }
 }
 assert.equal(count,67);
});
