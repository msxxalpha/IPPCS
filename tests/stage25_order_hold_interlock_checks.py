"""Stage 25 — SQL Server transaction-serialized manufacturing HOLD/RESUME acceptance.
No warehouse, purchasing, sales, HR, or external shop-floor integration.
"""

def stage25_acceptance(request,jalali,component_id):
    item=request("/api/v1/items","POST",{
        "code":"CI-ST25-QA-INTERLOCK","name":"Independent quality hold interlock product",
        "unit":"PCS","type":1},201)
    center=request("/api/v1/work-centers","POST",{
        "code":"CI-ST25-CELL","name":"Safe pause/resume work cell",
        "nominalUnitsPerHour":45},201)
    route=request("/api/v1/routings","POST",{
        "itemId":item["id"],"revisionCode":"CI-ST25-A",
        "effectiveFromJalali":jalali,"effectiveToJalali":None},201)
    request(f"/api/v1/routings/{route['id']}/operations","POST",{
        "sequence":10,"name":"Assembly / inspection hold interlock",
        "workCenterId":center["id"],"setupMinutes":1,
        "runMinutesPerUnit":2,"yieldPercent":100},201)
    request(f"/api/v1/routings/{route['id']}/approve","POST")
    bom=request("/api/v1/bom-revisions","POST",{
        "parentItemId":item["id"],"revision":"CI-ST25-A",
        "validFromJalali":jalali,"validUntilJalali":None},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/lines","POST",{
        "componentItemId":component_id,"quantityPer":1,
        "scrapPercent":0,"sequence":10},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/approve","POST")
    order=request("/api/v1/manufacturing-orders","POST",{
        "number":"CI-ST25-HOLD-INTERLOCK","itemId":item["id"],
        "plannedQuantity":5,"plannedStartJalali":jalali,
        "dueOnJalali":jalali},201)
    path=f"/api/v1/manufacturing-orders/{order['id']}"
    released=request(path+"/release","POST")
    assert released["status"]==1,released
    op=released["operations"][0]
    reporting=request(path+f"/operations/{op['id']}/confirm","POST",{
        "goodQuantity":1,"scrapQuantity":0,"reworkQuantity":0,
        "lotCode":"CI-ST25-LOT-1","note":"Partial report before QA containment"})
    assert reporting["status"]==2,reporting
    hold=request(path+"/quality-holds","POST",{
        "operationId":op["id"],"quantity":1,
        "reason":"CI defective fixture inspection"},201)
    request(path+"/hold","POST",{"reason":"Operator suspends line during QA containment"})
    held=request(path)
    assert held["status"]==3 and held["holdReason"],held
    request(path+"/hold","POST",{"reason":"Duplicate hold must fail"},409)
    request(path+"/resume","POST",status=409)
    assert request(path)["status"]==3
    request(path+"/quality-holds/"+hold["id"]+"/disposition?decision=1",
        "POST",{"note":"Quality inspector released isolated inspected unit"})
    resumed=request(path+"/resume","POST")
    assert resumed["status"]==2,resumed
    assert request(path)["holdReason"] is None
    request(path+"/resume","POST",status=409)
    actions=[x["action"] for x in request(path+"/history")["events"]]
    assert actions.count("Held")==1 and actions.count("Resumed")==1,actions
    assert "QualityHold" in actions and "QualityRelease" in actions,actions
    print("PASS: SQL guarded hold is single-transition, open QA quarantine blocks resume, disposition unlocks, and journal remains intact.")
