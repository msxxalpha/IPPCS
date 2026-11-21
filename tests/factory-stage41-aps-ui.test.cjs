const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web");
const read=file=>fs.readFileSync(path.join(root,file),"utf8");
const html=read("wwwroot/planning-advanced.html");
const js=read("wwwroot/js/planning-advanced.js");
const api=read("FactoryDemandSopAps.cs");
test("APS has selectable factory center, Jalali start/end, exact times and coded reason",()=>{
 for(const id of ["aps-slot-center","aps-slot-start","aps-slot-start-time",
   "aps-slot-end","aps-slot-end-time","aps-slot-reason-code",
   "aps-add-slot","aps-slot-list"])
   assert.ok(html.includes('id="'+id+'"'),id);
 assert.match(js,/unavailableSlots:apsSlots/);
 assert.match(js,/CapacityBlackoutReason/);
 assert.match(js,/aps-slot-reason-code/);
 assert.match(js,/IPPCSJalali\.parse/);
 assert.doesNotMatch(js,/\.innerHTML\s*=|insertAdjacentHTML/);
});
test("finite scheduling uses isolated half-open windows and preserves source snapshot",()=>{
 assert.match(api,/FactoryApsUnavailableSlot/);
 assert.match(api,/FactoryApsScenarioSlots\.TryWindow/);
 assert.match(api,/FactoryApsScenarioSlots\.Overlaps/);
 assert.match(api,/occupiedIntervals:evidence\.OccupiedIntervals\.Concat\(scenarioSlots\)/);
 assert.match(api,/scenarioUnavailableSlots=scenarioSlots\.Count/);
 assert.match(api,/unavailableSlots=inputs\.Select/);
 assert.match(api,/inputs\.Count>100/);
 assert.doesNotMatch(api,/\/api\/v1\/(warehouse|sales|purchasing|hr)/i);
});
