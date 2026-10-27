def stage19_acceptance(request, jalali, component):
    center=request("/api/v1/work-centers","POST",{
      "code":"CI-ST19-OEE-CENTER","name":"Stage19 unintegrated OEE center",
      "nominalUnitsPerHour":30},201)
    item=request("/api/v1/items","POST",{
      "code":"CI-ST19-OEE-SEMI","name":"Stage19 verified timed stops",
      "unit":"PCS","type":1},201)
    routing=request("/api/v1/routings","POST",{
      "itemId":item["id"],"revisionCode":"CI-ST19-ROUTING",
      "effectiveFromJalali":jalali,"effectiveToJalali":None},201)
    request(f"/api/v1/routings/{routing['id']}/operations","POST",{
      "sequence":10,"name":"Timed shop floor test",
      "workCenterId":center["id"],"setupMinutes":1,
      "runMinutesPerUnit":1,"yieldPercent":100},201)
    request(f"/api/v1/routings/{routing['id']}/approve","POST")
    bom=request("/api/v1/bom-revisions","POST",{
      "parentItemId":item["id"],"revision":"CI-ST19-BOM",
      "validFromJalali":jalali,"validUntilJalali":None},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/lines","POST",{
      "componentItemId":component,"quantityPer":1,"scrapPercent":0,
      "sequence":10},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/approve","POST")
    order=request("/api/v1/manufacturing-orders","POST",{
      "number":"CI-ST19-TIMED-STOP","itemId":item["id"],"plannedQuantity":10,
      "plannedStartJalali":jalali,"dueOnJalali":jalali},201)
    request("/api/v1/factory-cost/rates","PUT",{
      "workCenterId":center["id"],"conversionRialsPerMinute":100,
      "scrapRialsPerUnit":25,"effectiveFromJalali":jalali,
      "note":"CI original internal conversion assumptions"})
    released=request(f"/api/v1/manufacturing-orders/{order['id']}/release","POST")
    op=released["operations"][0]
    path=f"/api/v1/manufacturing-orders/{order['id']}"
    frozen=request(f"/api/v1/factory-cost/orders/{order['id']}/rate-snapshot")
    assert frozen["isHistoricalSnapshot"] and frozen["frozenOperations"]==1,frozen
    assert frozen["rates"][0]["conversionRialsPerMinute"]==100,frozen
    request("/api/v1/factory-cost/rates","PUT",{
      "workCenterId":center["id"],"conversionRialsPerMinute":999,
      "scrapRialsPerUnit":200,"effectiveFromJalali":jalali,
      "note":"CI updated rate must not reprice released order"})
    preserved=request(f"/api/v1/factory-cost/orders/{order['id']}/rate-snapshot")
    assert preserved["rates"][0]["conversionRialsPerMinute"]==100,preserved
    result=request(f"/api/v1/factory-cost/orders/{order['id']}/actual-variance")
    assert result["hasFrozenCostRates"],result
    assert result["rows"][0]["standardConversionRials"]==1100,result
    print("PASS: SQL release-time conversion rate is immutable after live center rate update; true historical cost not backfilled.")
    request("/api/v1/oee/plans","PUT",{
       "workCenterId":center["id"],"productionDateJalali":jalali,
       "plannedMinutes":120,"note":"CI declared internal day capacity"})
    begin=request(path+"/timed-downtime/start","POST",{
       "operationId":op["id"],"reason":"CI scheduled tool change"})
    assert begin["isOpen"],begin
    request(path+"/timed-downtime/start","POST",{
       "operationId":op["id"],"reason":"duplicate start"},409)
    request(path+f"/operations/{op['id']}/clock-start","POST",status=409)
    open_report=request(path+"/timed-downtime")
    assert len(open_report["rows"])==1 and open_report["rows"][0]["stoppedUtc"] is None,open_report
    after=request(path+f"/timed-downtime/{op['id']}/stop","POST")
    assert after["durationMinutes"]>0,after
    request(path+f"/timed-downtime/{op['id']}/stop","POST",status=409)
    start_clock=request(path+f"/operations/{op['id']}/clock-start","POST")
    assert start_clock["isRunning"],start_clock
    request(path+f"/operations/{op['id']}/clock-stop","POST")
    verified=request("/api/v1/oee/downtime-reconciliation?dateJalali="+jalali)
    row=next(x for x in verified["rows"] if x["workCenterId"]==center["id"])
    assert row["timedStopMinutes"] is not None and not row["hasOpenStop"],row
    assert row["legacyUntimedMinutes"]==0 and row["isTimeComparable"],row
    assert row["unclassifiedMinutes"]>=0,row
    print("PASS: SQL timed center stoppage exact once, clock start interlock and daily no-double-count OEE reconciliation.")

    request(path+"/downtime","POST",{
      "operationId":op["id"],"reason":"CI historic manual stop without clock boundaries",
      "durationMinutes":8},201)
    ambiguous=request("/api/v1/oee/downtime-reconciliation?dateJalali="+jalali)
    row=next(x for x in ambiguous["rows"] if x["workCenterId"]==center["id"])
    assert not row["isTimeComparable"] and row["status"]=="UnlocatedLegacyStop",row
    assert row["unclassifiedMinutes"] is None
    assert row["legacyUntimedMinutes"]==8,row
    print("PASS: SQL legacy duration-only downtime is explicitly unverified and never silently double-subtracted from actual clock.")
