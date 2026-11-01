def stage20_acceptance(request, jalali, tomorrow, component_id):
    from datetime import datetime
    from zoneinfo import ZoneInfo
    today=datetime.now(ZoneInfo("Asia/Tehran"))
    dow=(today.weekday()+1)%7     # .NET Sunday=0
    center=request("/api/v1/work-centers","POST",{
      "code":"CI-ST20-NIGHT-CENTER","name":"Overnight manufacturing stage20",
      "nominalUnitsPerHour":30},201)
    id=center["id"]
    shift=request("/api/v1/work-shifts","POST",{
      "workCenterId":id,"dayOfWeek":dow,"startTime":"22:00",
      "endTime":"06:00","effectiveCapacityPercent":100},201)
    assert shift["startTime"] and shift["endTime"],shift
    request("/api/v1/work-shifts","POST",{
      "workCenterId":id,"dayOfWeek":(dow+1)%7,"startTime":"05:30",
      "endTime":"07:00","effectiveCapacityPercent":100},409)
    request("/api/v1/work-shifts","POST",{
      "workCenterId":id,"dayOfWeek":dow,"startTime":"23:00",
      "endTime":"23:45","effectiveCapacityPercent":100},409)
    request("/api/v1/work-shifts","POST",{
      "workCenterId":id,"dayOfWeek":(dow+1)%7,"startTime":"06:00",
      "endTime":"07:00","effectiveCapacityPercent":100},201)
    print("PASS: SQL approved weekly midnight shift plus cross-weekday collision denied; next-day back-to-back allowed.")

    item=request("/api/v1/items","POST",{
      "code":"CI-ST20-NIGHT-ITEM","name":"Finished shock absorber for overnight MPS and finite slot",
      "unit":"PCS","type":2},201)
    route=request("/api/v1/routings","POST",{
      "itemId":item["id"],"revisionCode":"CI-ST20-ROUTE",
      "effectiveFromJalali":jalali,"effectiveToJalali":None},201)
    request(f"/api/v1/routings/{route['id']}/operations","POST",{
      "sequence":10,"name":"night shift op","workCenterId":id,
      "setupMinutes":1,"runMinutesPerUnit":1,"yieldPercent":100},201)
    request(f"/api/v1/routings/{route['id']}/approve","POST")
    control=request("/api/v1/control-plans","POST",{
      "routingRevisionId":route["id"],"code":"CI-ST20-CONTROL",
      "validFromJalali":jalali,"validUntilJalali":None},201)
    request(f"/api/v1/control-plans/{control['id']}/characteristics","POST",{
      "operationSequence":10,"code":"ST20-FINAL-CHECK",
      "description":"Overnight process quality check","unit":"N",
      "minimum":0,"maximum":100,"mandatory":True},201)
    request(f"/api/v1/control-plans/{control['id']}/approve","POST")
    bom=request("/api/v1/bom-revisions","POST",{
      "parentItemId":item["id"],"revision":"CI-ST20-BOM",
      "validFromJalali":jalali,"validUntilJalali":None},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/lines","POST",{
      "componentItemId":component_id,"quantityPer":1,"scrapPercent":0,
      "sequence":10},201)
    request(f"/api/v1/bom-revisions/{bom['id']}/approve","POST")
    order=request("/api/v1/manufacturing-orders","POST",{
      "number":"CI-ST20-NIGHT-ORDER","itemId":item["id"],
      "plannedQuantity":2,"plannedStartJalali":jalali,
      "dueOnJalali":tomorrow},201)
    operation=request(f"/api/v1/manufacturing-orders/{order['id']}/release","POST")["operations"][0]
    booking=request(f"/api/v1/manufacturing-orders/{order['id']}/capacity-reservations","POST",{
      "operationId":operation["id"],"dateJalali":jalali,
      "startTime":"23:50","endTime":"00:10","note":"CI midnight allocation"},201)
    assert booking["id"],booking
    listing=request(f"/api/v1/manufacturing-orders/{order['id']}/capacity-reservations")["rows"]
    row=next(x for x in listing if x["id"]==booking["id"])
    assert row["crossesMidnight"] and row["endDayJalali"]==tomorrow,row
    assert row["plannedMinutes"]==20,row
    print("PASS: SQL overnight capacity reservation survives immutable audit reconstruction with actual next-day end.")

    mps=request("/api/v1/mps","POST",{"code":"CI-ST20-NIGHT-MPS"},201)
    request(f"/api/v1/mps/{mps['id']}/lines","POST",{
      "itemId":item["id"],"neededOnJalali":tomorrow,
      "quantity":30,"reference":"CI-ST20-CRP-NIGHT"},201)
    request(f"/api/v1/mps/{mps['id']}/approve","POST")
    report=request(f"/api/v1/crp/{mps['id']}/preview")
    crp=next(x for x in report["rows"] if x["workCenterId"]==id)
    assert crp["capacityMinutes"]==420,crp   # 06:00 from overnight + 06:00..07:00 next shift
    assert crp["requiredMinutes"]==31,crp
    print("PASS: SQL CRP counts overnight carryover into following local date plus the next-day early shift.")
