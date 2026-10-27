def stage15_acceptance(request,today,tomorrow,component_item,mps_line):
    order=next(x for x in request("/api/v1/manufacturing-orders")
        if x["number"]=="CI-ST14-REPLAN")
    operation=order["operations"][0]
    confirm=request(
      f"/api/v1/manufacturing-orders/{order['id']}/operations/{operation['id']}/confirm",
      "POST",{"goodQuantity":1,"scrapQuantity":0,"reworkQuantity":0,
              "lotCode":"CI-ST15-ONE","note":"CI line side lot source"})
    assert confirm["status"]==2,confirm
    confirmations=request(f"/api/v1/manufacturing-orders/{order['id']}/confirmations")
    lot=next(x for x in confirmations if x["lotCode"]=="CI-ST15-ONE")
    path=f"/api/v1/factory-material/{order['id']}"
    original=request(path+"/lot-consumption","POST",{
      "confirmationId":lot["id"],"componentItemId":component_item,
      "quantity":1,"reference":"CI-ST15 real lot consumption"},201)
    request(path+"/lot-consumption","POST",{
      "confirmationId":lot["id"],"componentItemId":component_item,
      "quantity":1,"reference":"must exceed standard"},409)
    balances=request(path+"/lot/"+lot["id"])
    item=next(x for x in balances["rows"] if x["componentItemId"]==component_item)
    assert item["standardForReportedLot"]==1,item
    assert item["recordedNetQuantity"]==1,item
    entries=request(path+"/evidence-ledger")
    assert any(x["entryId"]==original["id"] and x["confirmationId"]==lot["id"]
        and x["netQuantity"]==1 for x in entries["rows"]),entries
    reversed=request(path+"/consumption/"+original["id"]+"/reverse","POST",{
      "reason":"CI supervisor validated wrong count"},201)
    assert reversed["quantityConsumed"]==-1,reversed
    request(path+"/consumption/"+original["id"]+"/reverse","POST",{
      "reason":"double reversal must fail"},409)
    final=request(path+"/lot/"+lot["id"])
    assert final["rows"][0]["recordedNetQuantity"]==0,final
    assert final["rows"][0]["activeEntries"]==0,final
    read=request(path+"/evidence-ledger")
    assert any(x["entryId"]==original["id"] and
      x["reversedById"]==reversed["id"] and x["recordedQuantity"]==1 and
      x["netQuantity"]==0 for x in read["rows"]),read
    print("PASS: SQL bounded lot-BOM consumption and single append-only correction retain immutable origin.")

    deviation=request("/api/v1/production-deviations","POST",{
      "mpsLineId":mps_line,"category":4,
      "description":"CI-ST15 capacity corrective action with escalation"},201)
    dev=f"/api/v1/production-deviations/{deviation['id']}"
    first={"rootCause":"machine maintenance window",
           "correctiveAction":"correct preventive routing step",
           "responsible":"internal shift controller","actionDueJalali":today}
    request(dev+"/action","POST",first)
    request(dev+"/action","POST",first,status=409)
    update=request(dev+"/revise-action","POST",{
      **first,"rootCause":"maintenance + tool mismatch",
      "correctiveAction":"verified alternate tool and shift review",
      "responsible":"internal planning controller",
      "actionDueJalali":today,
      "changeReason":"CI quality management approved replan"})
    assert update["status"]==1,update
    escalated=request(dev+"/escalate","POST",{
      "reason":"CI overdue evidence review","reviewByJalali":tomorrow})
    assert escalated["escalationCount"]==1,escalated
    request(dev+"/escalate","POST",{
      "reason":"CI overdue evidence review","reviewByJalali":tomorrow},409)
    follow=request(dev+"/follow-up?asOfJalali="+tomorrow)
    assert follow["isOverdue"] and follow["escalationCount"]==1,follow
    record=next(x for x in request("/api/v1/production-deviations")
        if x["id"]==deviation["id"])
    assert record["rootCause"]=="maintenance + tool mismatch"
    assert any("REVISION:OldRoot" in x["details"] for x in record["events"]),record
    assert any("REVISION:Reason" in x["details"] for x in record["events"]),record
    alerts=request("/api/v1/factory-kpis/production-alerts?asOfJalali="+
      tomorrow+"&daysAhead=7")
    assert any(x["referenceId"]==deviation["id"] and x["severity"]=="Critical"
               for x in alerts["alerts"]),alerts
    request(dev+"/close","POST",{"verification":"CI independent documented effectiveness"})
    request(dev+"/escalate","POST",{"reason":"closed cannot escalate"},409)
    cleaned=request("/api/v1/factory-kpis/production-alerts?asOfJalali="+
      tomorrow+"&daysAhead=7")
    assert not any(x["referenceId"]==deviation["id"] for x in cleaned["alerts"])
    print("PASS: SQL immutable corrective-action revision, nonduplicate escalation, critical alert and closure.")
