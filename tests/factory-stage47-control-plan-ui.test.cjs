const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const html=fs.readFileSync(path.join(root,"control-plan.html"),"utf8");
const js=fs.readFileSync(path.join(root,"js/control-plan.js"),"utf8");

test("every quality workspace navigation button opens an existing section",()=>{
 const buttons=[...html.matchAll(/<button[^>]*data-section="([^"]+)"/g)].map(x=>x[1]);
 const sections=[...html.matchAll(/<section id="([^"]+)" class="page"/g)].map(x=>x[1]);
 assert.equal(buttons.length,10);
 assert.equal(new Set(buttons).size,buttons.length,"navigation section IDs must be unique");
 for(const id of buttons)assert.ok(sections.includes(id),"missing section #"+id);
 assert.match(js,/section\.hidden=section\.id!==button\.dataset\.section/);
});

test("every static action button in the quality workspace is wired to a handler",()=>{
 const buttons=[...html.matchAll(/<button[^>]*id="([^"]+)"/g)].map(x=>x[1]);
 for(const id of buttons){
  assert.ok(js.includes("#"+id)||js.includes('"'+id+'"')||js.includes("'"+id+"'"),
   "button has no client binding: #"+id);
 }
 for(const id of ["inspection-refresh","link-refresh","assembly-refresh","readiness-refresh",
  "handoff-refresh","eol-refresh","eol-approve","factory-serial-reload",
  "spc-refresh","sampling-coverage-check","trace-refresh",
  "print-control-plan-button","print-eol-certificate"]){
  assert.ok(buttons.includes(id),"expected user action is absent: #"+id);
 }
});

test("quality forms have submit handlers and retain fail-closed sampling/SPC disclaimers",()=>{
 for(const id of ["plan-form","characteristic-form","inspection-form","lot-link-form",
  "assembly-origin-form","factory-serial-form","factory-serial-trace-form"]){
  assert.match(html,new RegExp('id="'+id+'"'),"missing form #"+id);
  assert.ok(js.includes('register("#'+id+'"'),"form is not wired: #"+id);
 }
 assert.match(js,/samplingCoverage/);
 assert.match(js,/برنامه نمونه‌برداری مصوب/);
 assert.match(js,/شواهد معتبر MSA/);
 assert.match(js,/تأیید قابلیت فرایند یا ترخیص/);
});
