"""Stage 34: shop-floor master coded stop and quality reasons and reporting.
All writes target disposable SQL Server inside consolidated CI only.
"""
from urllib.parse import quote

def stage34_acceptance(request, product_id, jalali):
    base="/api/v1/factory-master-options"
    # Seeded defaults live in basic data and can be administered.
    for kind,codes in [
        ("DowntimeReason",{"EQUIPMENT","SETUP","TOOLING","QUALITY","CAPACITY","OTHER"}),
        ("QualityHoldReason",{"MEASUREMENT","PROCESS","APPEARANCE","MATERIAL","ASSEMBLY","OTHER"})
    ]:
        options=request(base+"?kind="+kind)
        assert codes.issubset({x["code"] for x in options}),options
        assert all(x["isActive"] for x in options),options
    request(base,"POST",{
        "kind":"DowntimeReason","code":"EQUIPMENT",
        "label":"duplicate seeded code","sortOrder":10},409)
    route=next(x for x in request("/api/v1/routings")
        if x["itemId"]==product_id and x["state"]==1)
    order=request("/api/v1/manufacturing-orders","POST",{
        "number":"CI-ST34-SHOPFLOOR",
        "itemId":product_id,"plannedQuantity":12,
        "plannedStartJalali":jalali,"dueOnJalali":jalali},201)
    path=f"/api/v1/manufacturing-orders/{order['id']}"
    released=request(path+"/release","POST")
    op=released["operations"][0]
    opid=op["id"]
    request(path+"/downtime","POST",{
        "operationId":opid,"durationMinutes":7,
        "reason":"Hydraulic fixture fault",
        "reasonCode":"EQUIPMENT"},201)
    recorded=request(path+"/history")
    assert any("[EQUIPMENT]" in x["reason"] for x in recorded["downtime"]),recorded
    request(path+"/timed-downtime/start","POST",{
        "operationId":opid,"reason":"Minor setup adjustment",
        "reasonCode":"SETUP"})
    opened=request(path+"/timed-downtime")
    assert any("[SETUP]" in x["reason"] for x in opened["rows"]),opened
    request(path+f"/timed-downtime/{opid}/stop","POST")
    request(path+"/operations/"+opid+"/confirm","POST",{
        "goodQuantity":6,"scrapQuantity":0,"reworkQuantity":0,
        "lotCode":"CI-ST34-LOT"})
    hold=request(path+"/quality-holds","POST",{
        "operationId":opid,"quantity":2,
        "reason":"Damping measurement drift",
        "reasonCode":"MEASUREMENT"},201)
    holds=request(path+"/quality-holds")
    assert any(x["id"]==hold["id"] and "[MEASUREMENT]" in x["reason"] for x in holds),holds
    report=request(base+"/production-reason-summary?kind=QualityHoldReason"+
                   f"&fromJalali={quote(jalali)}&throughJalali={quote(jalali)}")
    matched=next(x for x in report["rows"] if x["code"]=="MEASUREMENT")
    assert matched["records"]>=1 and matched["measuredQuantity"]>=2,report
    downtime=request(base+"/production-reason-summary?kind=DowntimeReason"+
                     f"&fromJalali={quote(jalali)}&throughJalali={quote(jalali)}")
    assert next(x for x in downtime["rows"] if x["code"]=="EQUIPMENT")["measuredQuantity"]>=7,downtime
    assert next(x for x in downtime["rows"] if x["code"]=="SETUP")["records"]>=1,downtime
    request(base+"/production-reason-summary?kind=ActionOwner"+
            f"&fromJalali={quote(jalali)}&throughJalali={quote(jalali)}",status=400)
    request(base+"/production-reason-summary?kind=QualityHoldReason"+
            "&fromJalali=1405/15/99&throughJalali=1405/15/99",status=400)
    reason=next(x for x in request(base+"?kind=DowntimeReason")
        if x["code"]=="EQUIPMENT")
    request(base+"/"+reason["id"],"PUT",{
        "label":"خرابی ماشین - بازنگری","sortOrder":10,"isActive":False})
    request(path+"/downtime","POST",{
        "operationId":opid,"durationMinutes":4,
        "reason":"Should reject deactivated reason",
        "reasonCode":"EQUIPMENT"},409)
    report=request(base+"/production-reason-summary?kind=DowntimeReason"+
                   f"&fromJalali={quote(jalali)}&throughJalali={quote(jalali)}")
    classified=next(x for x in report["rows"] if x["code"]=="EQUIPMENT")
    assert classified["label"]=="خرابی ماشین - بازنگری"
    assert classified["active"] is False
    request(path+"/quality-holds","POST",{
        "operationId":opid,"quantity":1,"reason":"Invalid choice",
        "reasonCode":"UNKNOWN"},409)
    print("PASS: SQL classified downtime, timed stops, QA holds, deactivation interlock and stable-code dated reporting.")
