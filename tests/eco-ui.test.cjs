const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const html=fs.readFileSync(path.join(root,"engineering-rebind.html"),"utf8");
const js=fs.readFileSync(path.join(root,"js/engineering-rebind.js"),"utf8");
test("ECO UI provides independent planner request and quality-review controls",()=>{
 for(const id of ["eco-submit","eco-approval-select","eco-evidence",
                    "eco-observation","eco-approve","eco-reject","cutover-apply"]){
  assert.match(html,new RegExp('id="'+id+'"'));
 }
 assert.match(js,/eco-approvals/);
 assert.match(js,/quality-review/);
 assert.match(js,/ecoApprovalStatus==="QualityApproved"/);
});
