"""Stage 22: authenticated, disposable SQL Server engineering changeover end-to-end.
Only IPPCS planning and execution data, no ERP system integration.
"""

def stage22_acceptance(request, jalali, tomorrow, component_id):
    item=request("/api/v1/items","POST",{
        "code":"CI-ST22-ENGINEERED","name":"Engineering date-cutover semi-finished",
        "unit":"PCS","type":1},201)
    center=request("/api/v1/work-centers","POST",{
        "code":"CI-ST22-CELL","name":"Engineering dated work cell",
        "nominalUnitsPerHour":45},201)
    bom_old=request("/api/v1/bom-revisions","POST",{
        "parentItemId":item["id"],"revision":"CI-ST22-BOM-A",
        "validFromJalali":jalali,"validUntilJalali":None},201)
    request(f"/api/v1/bom-revisions/{bom_old['id']}/lines","POST",{
        "componentItemId":component_id,"quantityPer":1,"scrapPercent":0,
        "sequence":10},201)
    request(f"/api/v1/bom-revisions/{bom_old['id']}/approve","POST")
    route_old=request("/api/v1/routings","POST",{
        "itemId":item["id"],"revisionCode":"CI-ST22-ROUTE-A",
        "effectiveFromJalali":jalali,"effectiveToJalali":None},201)
    request(f"/api/v1/routings/{route_old['id']}/operations","POST",{
        "sequence":10,"name":"Old jig","workCenterId":center["id"],
        "setupMinutes":1,"runMinutesPerUnit":2,"yieldPercent":100},201)
    request(f"/api/v1/routings/{route_old['id']}/approve","POST")
    route_new=request("/api/v1/routings","POST",{
        "itemId":item["id"],"revisionCode":"CI-ST22-ROUTE-B",
        "effectiveFromJalali":tomorrow,"effectiveToJalali":None},201)
    request(f"/api/v1/routings/{route_new['id']}/operations","POST",{
        "sequence":10,"name":"New jig","workCenterId":center["id"],
        "setupMinutes":3,"runMinutesPerUnit":1,"yieldPercent":100},201)
    request(f"/api/v1/routings/{route_new['id']}/approve","POST",status=409)

    def order(code):
        return request("/api/v1/manufacturing-orders","POST",{
            "number":code,"itemId":item["id"],"plannedQuantity":3,
            "plannedStartJalali":tomorrow,"dueOnJalali":tomorrow},201)
    frozen=order("CI-ST22-FROZEN-ROUTE-A")
    before_release=request(f"/api/v1/manufacturing-orders/{frozen['id']}/release","POST")
    assert before_release["routingRevisionId"]==route_old["id"],before_release
    draft=order("CI-ST22-DRAFT-ROUTE-A")
    assert draft["routingRevisionId"]==route_old["id"],draft
    base={"kind":"Routing","baselineId":route_old["id"],
          "candidateId":route_new["id"],"cutoverOnJalali":tomorrow,
          "approvalReference":"ECO-CI-ST22-ROUTING",
          "reason":"Approved internal future routing change"}
    preview=request("/api/v1/factory-engineering/cutover","POST",
                    {**base,"dryRun":True})
    assert preview["dryRun"] and preview["protectedFrozenOrders"]>=1,preview
    assert any(x["id"]==draft["id"] for x in preview["staleDraftOrders"]),preview
    applied=request("/api/v1/factory-engineering/cutover","POST",
                    {**base,"dryRun":False})
    assert applied["journalId"],applied
    request("/api/v1/factory-engineering/cutover","POST",
            {**base,"dryRun":False},409)
    versions=request("/api/v1/routings")
    old=next(x for x in versions if x["id"]==route_old["id"])
    new=next(x for x in versions if x["id"]==route_new["id"])
    assert old["effectiveToJalali"]==jalali and new["state"]==1,(old,new)
    assert request(f"/api/v1/manufacturing-orders/{frozen['id']}")["routingRevisionId"]==route_old["id"]
    assert request(f"/api/v1/manufacturing-orders/{draft['id']}")["routingRevisionId"]==route_old["id"]
    records=request(f"/api/v1/factory-engineering/cutovers?itemId={item['id']}")
    assert any(x["id"]==applied["journalId"] and x["kind"]=="Routing"
               for x in records),records

    move={"kind":"Routing","baselineId":route_old["id"],
          "candidateId":route_new["id"],"orderIds":[draft["id"]],
          "reason":"CI engineering changeout","dryRun":True}
    checked=request("/api/v1/factory-engineering/draft-rebind","POST",move)
    assert checked["rows"][0]["canRebind"],checked
    moved=request("/api/v1/factory-engineering/draft-rebind","POST",
                  {**move,"dryRun":False})
    assert moved["applied"]==1,moved
    after=request(f"/api/v1/manufacturing-orders/{draft['id']}")
    assert after["routingRevisionId"]==route_new["id"] and after["status"]==0,after
    history=request(f"/api/v1/manufacturing-orders/{draft['id']}/history")["events"]
    assert any(x["action"]=="EngineeringDraftRebind"
               and route_old["id"].replace("-","") in x["details"]
               for x in history),history
    request("/api/v1/factory-engineering/draft-rebind","POST",move,409)
    final=request(f"/api/v1/manufacturing-orders/{draft['id']}/release","POST")
    assert final["routingRevisionId"]==route_new["id"],final
    assert final["operations"][0]["setupMinutes"]==3,final
    print("PASS: SQL routing effective cutover, immutable audit, frozen old order, separately approved new draft.")

    # Independent BOM cutover with the SAME existing item and a new draft
    # referencing the old BOM, while both previously released orders stay frozen.
    bom_new=request("/api/v1/bom-revisions","POST",{
        "parentItemId":item["id"],"revision":"CI-ST22-BOM-B",
        "validFromJalali":tomorrow,"validUntilJalali":None},201)
    request(f"/api/v1/bom-revisions/{bom_new['id']}/lines","POST",{
        "componentItemId":component_id,"quantityPer":2,
        "scrapPercent":0,"sequence":10},201)
    request(f"/api/v1/bom-revisions/{bom_new['id']}/approve","POST",status=409)
    bom_draft=order("CI-ST22-DRAFT-BOM-A")
    assert bom_draft["bomRevisionId"]==bom_old["id"]
    bom_change={"kind":"BOM","baselineId":bom_old["id"],"candidateId":bom_new["id"],
                "cutoverOnJalali":tomorrow,"approvalReference":"ECO-CI-ST22-BOM",
                "reason":"Approved internal BOM quantity update"}
    bom_preview=request("/api/v1/factory-engineering/cutover","POST",
                        {**bom_change,"dryRun":True})
    assert bom_preview["protectedFrozenOrders"]>=2,bom_preview
    committed=request("/api/v1/factory-engineering/cutover","POST",
                      {**bom_change,"dryRun":False})
    assert committed["journalId"],committed
    bom_move={"kind":"BOM","baselineId":bom_old["id"],
              "candidateId":bom_new["id"],"orderIds":[bom_draft["id"]],
              "reason":"CI approved BOM change","dryRun":False}
    assert request("/api/v1/factory-engineering/draft-rebind","POST",
                   bom_move)["applied"]==1
    assert request(f"/api/v1/manufacturing-orders/{bom_draft['id']}")["bomRevisionId"]==bom_new["id"]
    release=request(f"/api/v1/manufacturing-orders/{bom_draft['id']}/release","POST")
    assert release["routingRevisionId"]==route_new["id"] and release["bomRevisionId"]==bom_new["id"]
    assert request(f"/api/v1/manufacturing-orders/{frozen['id']}")["bomRevisionId"]==bom_old["id"]
    print("PASS: SQL BOM effective cutover and draft rebind preserve previously released revision linkage.")
