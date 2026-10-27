def stage16_acceptance(request, jalali, center_id, semi_item):
    # SQL-backed two-step final shock absorber routing and approved final
    # inspection. All entities are internal factory records, never warehouse.
    source=request("/api/v1/manufacturing-orders","POST",{
      "number":"CI-ST16-NEW-SEMI","itemId":semi_item,"plannedQuantity":4,
      "plannedStartJalali":jalali,"dueOnJalali":jalali},201)
    source_ops=request(f"/api/v1/manufacturing-orders/{source['id']}/release",
      "POST")["operations"]
    request(f"/api/v1/manufacturing-orders/{source['id']}/operations/{source_ops[0]['id']}/confirm",
      "POST",{"goodQuantity":4,"scrapQuantity":0,"reworkQuantity":0,
              "lotCode":"CI-ST16-SEMI-OUT"})
    source_lot=next(x for x in request(
      f"/api/v1/manufacturing-orders/{source['id']}/confirmations")
      if x["lotCode"]=="CI-ST16-SEMI-OUT")

    item=request("/api/v1/items","POST",{
      "code":"CI-ST16-FINAL","name":"Stage16 final dampers",
      "unit":"PCS","type":2},201)
    route=request("/api/v1/routings","POST",{
      "itemId":item["id"],"revisionCode":"CI-ST16-ROUTING",
      "effectiveFromJalali":jalali,"effectiveToJalali":None},201)
    for sequence in [10,20]:
        request(f"/api/v1/routings/{route['id']}/operations","POST",{
          "sequence":sequence,"name":"CI-Stage16-"+str(sequence),
          "workCenterId":center_id,"setupMinutes":1,
          "runMinutesPerUnit":1,"yieldPercent":100},201)
    request(f"/api/v1/routings/{route['id']}/approve","POST")
    bom=request("/api/v1/bom-revisions","POST",{
      "parentItemId":item["id"],"revision":"CI-ST16-FROZEN-BOM",
      "validFromJalali":jalali,"validUntilJalali":None},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/lines","POST",{
      "componentItemId":semi_item,"quantityPer":2,"scrapPercent":0,
      "sequence":10},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/approve","POST")
    control=request("/api/v1/control-plans","POST",{
      "routingRevisionId":route["id"],"code":"CI-ST16-END-OF-LINE",
      "validFromJalali":jalali,"validUntilJalali":None},201)
    request(f"/api/v1/control-plans/{control['id']}/characteristics","POST",{
      "operationSequence":20,"code":"DAMPING-FORCE-ST16",
      "description":"Shock absorber end-of-line measured damping force",
      "unit":"N","minimum":50,"maximum":60,"mandatory":True},201)
    request(f"/api/v1/control-plans/{control['id']}/approve","POST")
    final=request("/api/v1/manufacturing-orders","POST",{
      "number":"CI-ST16-FINAL","itemId":item["id"],"plannedQuantity":2,
      "plannedStartJalali":jalali,"dueOnJalali":jalali},201)
    ops=sorted(request(f"/api/v1/manufacturing-orders/{final['id']}/release",
         "POST")["operations"],key=lambda x:x["sequence"])
    for op,code in zip(ops,["CI-ST16-FIRST-LOT","CI-ST16-FINAL-LOT"]):
        request(f"/api/v1/manufacturing-orders/{final['id']}/operations/{op['id']}/confirm",
          "POST",{"goodQuantity":2,"scrapQuantity":0,"reworkQuantity":0,"lotCode":code})
    lots=request(f"/api/v1/manufacturing-orders/{final['id']}/confirmations")
    first=next(x for x in lots if x["lotCode"]=="CI-ST16-FIRST-LOT")
    last=next(x for x in lots if x["lotCode"]=="CI-ST16-FINAL-LOT")
    internal=f"/api/v1/manufacturing-orders/{final['id']}/lot-genealogy"
    request(internal,"POST",{
      "sourceConfirmationId":first["id"],"destinationConfirmationId":last["id"],
      "quantity":2},409)
    pending=request(f"/api/v1/manufacturing-orders/{final['id']}/end-of-line-check")
    assert not pending["canApprove"] and any(
      x["code"]=="FinalQualityChecks" for x in pending["issues"]),pending
    characteristic=request(
      f"/api/v1/manufacturing-orders/{final['id']}/inspection-requirements")["requirements"][0]
    measured=request(
      f"/api/v1/manufacturing-orders/{final['id']}/inspections/{characteristic['id']}",
      "POST",{"value":55,"nonconformingQuantity":0,"note":"CI measured damping force"})
    assert measured["passed"] and measured["orderStatus"]==4,measured
    handoff=request(f"/api/v1/manufacturing-orders/{final['id']}/handoff-audit")
    assert handoff["pairsWithUntracedOutput"]==1, handoff
    request(f"/api/v1/manufacturing-orders/{final['id']}/approve-end-of-line",
       "POST",status=409)
    request(internal,"POST",{
      "sourceConfirmationId":first["id"],"destinationConfirmationId":last["id"],
      "quantity":2},201)
    handoff=request(f"/api/v1/manufacturing-orders/{final['id']}/handoff-audit")
    assert (handoff["pairsWithUntracedOutput"]==0 and
      handoff["totalUntracedDownstreamGood"]==0),handoff
    pending=request(f"/api/v1/manufacturing-orders/{final['id']}/end-of-line-check")
    assert any(x["code"]=="MissingSemiOrigin" for x in pending["issues"]),pending
    cross=f"/api/v1/manufacturing-orders/{final['id']}/assembly-genealogy"
    request(cross,"POST",{
      "semiFinishedConfirmationId":source_lot["id"],
      "assemblyConfirmationId":last["id"],
      "componentQuantity":4},201)
    ready=request(f"/api/v1/manufacturing-orders/{final['id']}/end-of-line-check")
    assert ready["canApprove"] and ready["finalLotCount"]==1,ready
    assert ready["finalGoodQuantity"]==2 and ready["mandatoryFinalChecks"]==1,ready
    assert ready["untracedStagePairs"]==0,ready
    approved=request(f"/api/v1/manufacturing-orders/{final['id']}/approve-end-of-line",
      "POST")
    assert approved["approved"] and approved["finalGoodQuantity"]==2,approved
    signed=request(f"/api/v1/manufacturing-orders/{final['id']}/end-of-line-check")
    assert signed["alreadyApproved"] and not signed["canApprove"],signed
    request(f"/api/v1/manufacturing-orders/{final['id']}/approve-end-of-line","POST",status=409)
    request(internal,"POST",{
      "sourceConfirmationId":first["id"],"destinationConfirmationId":last["id"],
      "quantity":1},409)
    request(cross,"POST",{
      "semiFinishedConfirmationId":source_lot["id"],
      "assemblyConfirmationId":last["id"],"componentQuantity":1},409)
    print("PASS: SQL finished shock absorber quality gate, documented operation handoff, exact semi-BOM provenance and immutable EOL signature.")
