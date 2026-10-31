"""Stage 37 SQL integration: typed factory APS blackout reasons and authorization.
It never writes to the warehouse, purchasing, sales, or HR modules.
"""

def stage37_acceptance(request, mps_id, center_id, jalali):
    catalog="/api/v1/factory-master-options"
    options=request(catalog+"?kind=CapacityBlackoutReason&includeInactive=true")
    codes={x["code"] for x in options}
    assert {"MAINTENANCE","CALIBRATION","CHANGEOVER","ENERGY","SAFETY","OTHER"}<=codes,options
    selected=next(x for x in options if x["code"]=="MAINTENANCE")
    payload={
        "mpsVersionId":mps_id,"startJalali":jalali,"horizonDays":7,
        "capacityMultiplier":1,
        "unavailableDays":[{
            "workCenterId":center_id,"dateJalali":jalali,
            "reason":selected["label"],"reasonCode":"MAINTENANCE"
        }]
    }
    def post(payload,status):
        return request("/api/v1/factory-aps/calculate","POST",payload,status)
    post({**payload,"unavailableDays":[{
        **payload["unavailableDays"][0],"reasonCode":"NOT_IN_CATALOG"}]},409)
    post({**payload,"unavailableDays":[{
        **payload["unavailableDays"][0],"reason":"wrong display label"}]},409)
    request(catalog+"/"+selected["id"],"PUT",{
        "label":selected["label"],"sortOrder":selected["sortOrder"],
        "isActive":False})
    post(payload,409)
    request(catalog+"/"+selected["id"],"PUT",{
        "label":selected["label"],"sortOrder":selected["sortOrder"],
        "isActive":True})
    assert "MAINTENANCE" in {x["code"] for x in request(
        catalog+"?kind=CapacityBlackoutReason")}
    # Stage 41: machine setup is a half-open local interval in the scenario,
    # not a full-day shutdown or a mutation to the approved MPS.
    slot={"workCenterId":center_id,"startJalali":jalali,
        "startTime":"10:00","endJalali":jalali,"endTime":"12:00",
        "reason":selected["label"],"reasonCode":"MAINTENANCE"}
    partial={**payload,"unavailableDays":[],"unavailableSlots":[slot]}
    post({**partial,"unavailableSlots":[{**slot,"endTime":"09:00"}]},400)
    post({**partial,"unavailableSlots":[{**slot,"reasonCode":"NOT_IN_CATALOG"}]},409)
    post({**partial,"unavailableSlots":[slot,{**slot,"startTime":"11:00","endTime":"13:00"}]},409)
    computed=post(partial,200)
    assert computed["executionEvidence"]["scenarioUnavailableSlots"]==1,computed
    assert request(f"/api/v1/factory-aps/runs/{computed['runId']}")["result"]
    print("PASS: APS blackout master codes, title consistency, deactivated and unrecognized code interlocks.")
    print("PASS: SQL valid timed APS blackout, overlap, invalid time and master-code guards.")
    # Stage 42: tool groups are IPPCS-owned lookup values, never equipment
    # calibration or inventory assets. Invalid choices fail closed.
    fixture=request(catalog,"POST",{"kind":"SharedToolGroup",
        "code":"CI_SHARED_FIXTURE","label":"CI shared damper fixture","sortOrder":1},201)
    # Use a fresh center for the catalog/assignment check. Earlier E2E stages
    # deliberately leave real operation clocks and reservations on center_id;
    # binding that busy center to a new shared tool would correctly make the
    # scheduler reject overlapping historical occupancy and would conflate
    # catalog validation with the independent overlap-safety test.
    tool_center=request("/api/v1/work-centers","POST",{
        "code":"CI-APS-TOOL-CENTER","name":"CI APS shared-tool validation bench",
        "nominalUnitsPerHour":60
    },201)
    tool={"workCenterId":tool_center["id"],"toolGroupCode":"CI_SHARED_FIXTURE"}
    post({**partial,"sharedTools":[{**tool,"toolGroupCode":"NOT_IN_CATALOG"}]},409)
    post({**partial,"sharedTools":[tool,tool]},400)
    result=post({**partial,"sharedTools":[tool]},200)
    assert result["executionEvidence"]["sharedToolAssignments"]==1,result
    request(catalog+"/"+fixture["id"],"PUT",{
        "label":"CI shared damper fixture","sortOrder":1,"isActive":False})
    post({**partial,"sharedTools":[tool]},409)
    print("PASS: SQL APS shared-tool active catalog selection, duplicate assignment and deactivation checks.")
