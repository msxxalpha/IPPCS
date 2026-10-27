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
    print("PASS: APS blackout master codes, title consistency, deactivated and unrecognized code interlocks.")
