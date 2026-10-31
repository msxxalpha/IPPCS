def stage14_acceptance(request, jalali, tomorrow, semi_item, component_item, center_id):
    # Revision approval must reject an overlapping effective interval both at
    # read-only preflight and at actual serialized write time.
    route=request("/api/v1/routings","POST",{
        "itemId":semi_item,"revisionCode":"CI-ST14-OVERLAP",
        "effectiveFromJalali":jalali,"effectiveToJalali":None},201)
    request(f"/api/v1/routings/{route['id']}/operations","POST",{
        "sequence":10,"name":"CI duplicate candidate",
        "workCenterId":center_id,"setupMinutes":2,
        "runMinutesPerUnit":1,"yieldPercent":100},201)
    route_check=request(f"/api/v1/factory-revisions/routings/{route['id']}/approval-check")
    assert not route_check["canApprove"]
    assert any(x["code"]=="ApprovedOverlap" for x in route_check["issues"]),route_check
    request(f"/api/v1/routings/{route['id']}/approve","POST",status=409)
    print("PASS: SQL Routing preflight and transactional approval reject version overlap.")

    bom=request("/api/v1/bom-revisions","POST",{
        "parentItemId":semi_item,"revision":"CI-ST14-OVERLAP",
        "validFromJalali":jalali,"validUntilJalali":None},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/lines","POST",{
        "componentItemId":component_item,"quantityPer":1,
        "scrapPercent":0,"sequence":10},201)
    bom_check=request(f"/api/v1/factory-revisions/bom/{bom['id']}/approval-check")
    assert not bom_check["canApprove"]
    assert any(x["code"]=="ApprovedOverlap" for x in bom_check["issues"]),bom_check
    request(f"/api/v1/bom-revisions/{bom['id']}/approve","POST",status=409)
    print("PASS: SQL BOM preflight and atomic approval reject overlapping active revisions.")

    draft=request("/api/v1/manufacturing-orders","POST",{
      "number":"CI-ST14-REPLAN","itemId":semi_item,"plannedQuantity":5,
      "plannedStartJalali":jalali,"dueOnJalali":tomorrow},201)
    preflight=request(f"/api/v1/manufacturing-orders/{draft['id']}/release-check")
    assert preflight["canRelease"] and preflight["operations"]==1,preflight
    modified=request(f"/api/v1/manufacturing-orders/{draft['id']}/replan-draft",
       "POST",{"plannedQuantity":3,"plannedStartJalali":tomorrow,
       "dueOnJalali":tomorrow,"reason":"CI operational date and quantity change"})
    assert modified["plannedQuantity"]==3 and modified["plannedStartJalali"]==tomorrow
    assert request(f"/api/v1/manufacturing-orders/{draft['id']}/release-check")["canRelease"]
    events=request(f"/api/v1/manufacturing-orders/{draft['id']}/history")["events"]
    assert any(x["action"]=="DraftReplanned" and
               "CI operational date" in (x["details"] or "") and
               "Before:" in x["details"] and "After:" in x["details"] and
               "By=" in x["details"] for x in events)
    released=request(f"/api/v1/manufacturing-orders/{draft['id']}/release","POST")
    assert released["status"]==1 and len(released["operations"])==1,released
    request(f"/api/v1/manufacturing-orders/{draft['id']}/replan-draft","POST",{
       "plannedQuantity":5,"plannedStartJalali":jalali,"dueOnJalali":tomorrow,
       "reason":"must be blocked after release"},status=409)
    blocked=request(f"/api/v1/manufacturing-orders/{draft['id']}/release-check")
    assert not blocked["canRelease"] and any(
        x["code"]=="NotDraft" for x in blocked["issues"]),blocked
    print("PASS: SQL draft replan audits original dates/quantity; release freezes routing and blocks replan.")

    board=request("/api/v1/factory-dispatch/board?asOfJalali="+tomorrow+"&horizonDays=7")
    row=next(x for x in board["rows"] if x["orderId"]==draft["id"])
    assert row["sequence"]==10 and row["canStartByRouteAndQuality"],row
    assert not row["hasBookedMachineSlot"],row
    assert board["missingReservationCount"]>=1,board
    assert request("/api/v1/factory-dispatch/board?asOfJalali=1405/13/40&horizonDays=7",
        status=400)["message"]
    print("PASS: SQL dispatch board honors live operation/quality gate and reports missing machine reservation.")
