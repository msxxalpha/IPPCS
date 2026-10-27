"""Stage 21: disposable SQL negative-path checks for immutable engineering cutover.
No warehouse, purchasing, sales, HR, or external database calls.
Positive revision migration requires a separately authorized ECO cutover;
regular approval correctly forbids overlapping effective dates.
"""

def stage21_acceptance(request, jalali, tomorrow, component_id):
    item=request("/api/v1/items","POST",{
        "code":"CI-ST21-ENGINEERING","name":"Manufacturing engineering rebind safety",
        "unit":"PCS","type":1},201)
    center=request("/api/v1/work-centers","POST",{
        "code":"CI-ST21-ENG-CENTER","name":"Engineering safety cell",
        "nominalUnitsPerHour":50},201)
    bom=request("/api/v1/bom-revisions","POST",{
        "parentItemId":item["id"],"revision":"CI-ST21-BOM",
        "validFromJalali":jalali,"validUntilJalali":None},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/lines","POST",{
        "componentItemId":component_id,"quantityPer":1,
        "scrapPercent":0,"sequence":10},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/approve","POST")
    def route(code,from_date,to_date):
        r=request("/api/v1/routings","POST",{
            "itemId":item["id"],"revisionCode":code,
            "effectiveFromJalali":from_date,"effectiveToJalali":to_date},201)
        request(f"/api/v1/routings/{r['id']}/operations","POST",{
            "sequence":10,"name":"Independent cell",
            "workCenterId":center["id"],"setupMinutes":1,
            "runMinutesPerUnit":1,"yieldPercent":100},201)
        request(f"/api/v1/routings/{r['id']}/approve","POST")
        return r
    baseline=route("CI-ST21-OLD",jalali,jalali)
    candidate=route("CI-ST21-NEW",tomorrow,None)
    def order(code,date):
        return request("/api/v1/manufacturing-orders","POST",{
            "number":code,"itemId":item["id"],"plannedQuantity":4,
            "plannedStartJalali":date,"dueOnJalali":tomorrow},201)
    old=order("CI-ST21-OLD-DRAFT",jalali)
    new=order("CI-ST21-NEW-DRAFT",tomorrow)
    assert old["routingRevisionId"]==baseline["id"],old
    assert new["routingRevisionId"]==candidate["id"],new

    def body(order_ids,apply=False):
        return {"kind":"Routing","baselineId":baseline["id"],
                "candidateId":candidate["id"],"orderIds":order_ids,
                "reason":"CI approved date guard", "dryRun":not apply}
    # Even a properly approved destination cannot rewrite an order outside
    # that revision's dates; preview and apply both fail closed.
    for apply in (False,True):
        conflict=request("/api/v1/factory-engineering/draft-rebind",
                         "POST",body([old["id"]],apply),409)
        assert not conflict["rows"][0]["canRebind"],conflict
        assert any(x["code"]=="RoutingUnavailable"
                   for x in conflict["rows"][0]["issues"]),conflict
    # Group update is atomic: one valid order cannot rescue another order
    # on the wrong baseline or outside the candidate's validity.
    conflict=request("/api/v1/factory-engineering/draft-rebind",
                     "POST",body([old["id"],new["id"]],True),409)
    assert len(conflict["rows"])==2,conflict
    assert request(f"/api/v1/manufacturing-orders/{old['id']}")["routingRevisionId"]==baseline["id"]
    assert request(f"/api/v1/manufacturing-orders/{new['id']}")["routingRevisionId"]==candidate["id"]
    request(f"/api/v1/manufacturing-orders/{old['id']}/release","POST")
    blocked=request("/api/v1/factory-engineering/draft-rebind",
                    "POST",body([old["id"]],True),409)
    assert any(x["code"]=="OrderNotDraft"
               for x in blocked["rows"][0]["issues"]),blocked
    history=request(f"/api/v1/manufacturing-orders/{old['id']}/history")["events"]
    assert not any(x["action"]=="EngineeringDraftRebind" for x in history),history
    print("PASS: SQL engineering rebind is atomic, date-guarded and rejects released or mismatched orders.")
