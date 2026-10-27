def stage18_acceptance(request, jalali, fixture, center_id, semi_item):
    order=fixture["finalOrderId"]
    lot=fixture["finalLotId"]
    base=f"/api/v1/manufacturing-orders/{order}/serials"
    assert request(base)["assigned"]==0
    request(base,"POST",{"confirmationId":fixture["semiLotId"],
      "serialNumber":"CI-ST18-WRONG-LOT"},409)
    request(base,"POST",{"confirmationId":lot,"serialNumber":"INVALID;SERIAL"},400)
    first=request(base,"POST",{
      "confirmationId":lot,"serialNumber":"ci-st18-1405-001"},201)
    assert first["serialNumber"]=="CI-ST18-1405-001",first
    request(base,"POST",{
      "confirmationId":lot,"serialNumber":"CI-ST18-1405-001"},409)
    second=request(base,"POST",{
      "confirmationId":lot,"serialNumber":"CI-ST18-۱۴۰۵-۰۰۲"},201)
    assert second["serialNumber"]=="CI-ST18-1405-002",second
    request(base,"POST",{"confirmationId":lot,"serialNumber":"CI-ST18-1405-003"},409)
    serials=request(base)
    assert serials["assigned"]==2 and serials["finishedLotCount"]==1,serials
    assert {x["serialNumber"] for x in serials["serials"]}=={
        first["serialNumber"],second["serialNumber"]},serials
    origin=request("/api/v1/factory-serials/ci-st18-1405-001/trace?depth=8")
    assert origin["serial"]["confirmationId"]==lot,origin
    ancestry=origin["ancestry"]
    assert ancestry["hasLinks"] and ancestry["orderCount"]>=2,ancestry
    assert any(x["confirmationId"]==fixture["semiLotId"] for x in ancestry["nodes"]),ancestry
    request("/api/v1/factory-serials/CI-ST18-UNKNOWN/trace",status=404)
    print("PASS: SQL post-EOL serialized unique production serials; signed lot ceiling, Persian digit normalization and multi-order backward ancestry.")

    bom=request("/api/v1/bom-revisions","POST",{
       "parentItemId":fixture["finalItemId"],"revision":"CI-ST18-PROPOSED-BOM",
       "validFromJalali":jalali,"validUntilJalali":None},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/lines","POST",{
       "componentItemId":semi_item,"quantityPer":3,"scrapPercent":0,
       "sequence":10},201)
    bom_report=request(
      f"/api/v1/factory-engineering/bom/{bom['id']}/impact?baselineId="+fixture["bomId"])
    assert not bom_report["readyForRegularApproval"],bom_report
    assert bom_report["overlappingEffectiveDates"],bom_report
    assert any(x["code"]=="ApprovedOverlap" for x in bom_report["issues"]),bom_report
    assert any(x["componentId"]==semi_item and x["oldQuantity"]==2 and
               x["newQuantity"]==3 for x in bom_report["parts"]),bom_report
    assert bom_report["affectedOrders"]>=1 and bom_report["activeOrCompletedOrders"]>=1,bom_report
    request(f"/api/v1/bom-revisions/{bom['id']}/approve","POST",status=409)
    print("PASS: SQL engineering BOM change flags coefficient difference, impacted frozen production and blocked overlapping approval.")

    routing=request("/api/v1/routings","POST",{
        "itemId":fixture["finalItemId"],"revisionCode":"CI-ST18-ROUTING-ECO",
        "effectiveFromJalali":jalali,"effectiveToJalali":None},201)
    for sequence in [10,20]:
       request(f"/api/v1/routings/{routing['id']}/operations","POST",{
         "sequence":sequence,"name":"CI-ST18-ECO-STEP-"+str(sequence),
         "workCenterId":center_id,"setupMinutes":1,
         "runMinutesPerUnit":2 if sequence==20 else 1,"yieldPercent":100},201)
    impact=request(f"/api/v1/factory-engineering/routing/{routing['id']}/impact?baselineId="+
                   fixture["routeId"])
    assert impact["overlappingEffectiveDates"] and not impact["readyForRegularApproval"],impact
    assert any(x["code"]=="ApprovedOverlap" for x in impact["issues"]),impact
    assert any(x["sequence"]==20 and x["oldMinutesPerUnit"]==1 and
               x["newMinutesPerUnit"]==2 for x in impact["operations"]),impact
    assert impact["affectedOrders"]>=1,impact
    request(f"/api/v1/routings/{routing['id']}/approve","POST",status=409)
    print("PASS: SQL engineering routing identifies changed cycle time, affected frozen orders and refuses nonapproved ECO overlap.")
