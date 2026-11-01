"""Stage 33: versioned, reportable factory master-data acceptance on disposable SQL.
No warehouse, purchasing, sales, HR, or external-master connector is used.
"""
from urllib.parse import quote

def stage33_acceptance(request, mps_line_id, jalali):
    prefix="/api/v1/factory-master-options"
    categories=request(prefix+"?kind=DeviationCategory&includeInactive=true")
    assert {x["code"] for x in categories}=={str(i) for i in range(7)},categories
    roots=request(prefix+"?kind=RootCauseCategory")
    assert {"PROCESS","MACHINE","METHOD","MATERIAL","MEASUREMENT","CAPACITY","OTHER"}<={
        x["code"] for x in roots},roots
    request(prefix+"?kind=UnknownKind",status=400)
    request(prefix,"POST",{"kind":"ActionOwner","code":"NON-VALID SPACE",
        "label":"Invalid code","sortOrder":10},400)

    owner=request(prefix,"POST",{"kind":"ActionOwner","code":"shift_lead",
        "label":"مسئول شیفت تولید","sortOrder":10},201)
    assert owner["code"]=="SHIFT_LEAD" and owner["isActive"],owner
    request(prefix,"POST",{"kind":"ActionOwner","code":"SHIFT_LEAD",
        "label":"مسئول تکراری","sortOrder":11},409)
    another=request(prefix,"POST",{"kind":"ActionOwner","code":"QA_TEAM",
        "label":"گروه کنترل کیفیت","sortOrder":20},201)
    assert another["code"]=="QA_TEAM",another
    before=request(prefix+"?kind=ActionOwner")
    assert len(before)==2,before
    changed=request(prefix+"/"+owner["id"],"PUT",{
        "label":"سرپرست شیفت تولید","isActive":True,"sortOrder":5})
    assert changed["code"]=="SHIFT_LEAD" and changed["label"]=="سرپرست شیفت تولید",changed

    unique="CI-ST33 master catalog selectable deviation"
    entry=request("/api/v1/production-deviations","POST",{
        "mpsLineId":mps_line_id,"category":4,"description":unique},201)
    assert entry["category"]==4,entry
    path=f"/api/v1/production-deviations/{entry['id']}"
    request(path+"/action","POST",{
        "rootCause":"Temporary fixture capacity restriction",
        "correctiveAction":"Reschedule the production shift",
        "responsible":"not authoritative when master code exists",
        "responsibleCode":"SHIFT_LEAD",
        "rootCauseCategoryCode":"CAPACITY",
        "actionDueJalali":jalali})
    row=next(x for x in request("/api/v1/production-deviations")
             if x["id"]==entry["id"])
    assert row["responsible"]=="سرپرست شیفت تولید",row
    assert row["responsibleCode"]=="SHIFT_LEAD",row
    assert row["rootCauseCategoryCode"]=="CAPACITY",row

    term=quote(unique)
    report=request(f"/api/v1/production-deviations/search?text={term}&category=4&status=1&page=1&pageSize=1")
    assert report["total"]==1 and report["totalPages"]==1 and len(report["rows"])==1,report
    assert report["rows"][0]["id"]==entry["id"],report
    multi=request(f"/api/v1/production-deviations/search?text={term}&categories=2,4&status=1&page=1&pageSize=10")
    assert multi["total"]==1 and multi["rows"][0]["id"]==entry["id"],multi
    request("/api/v1/production-deviations/search?categories=4,99&page=1&pageSize=20",status=400)
    request("/api/v1/production-deviations/search?categories=4&category=4&page=1&pageSize=20",status=400)
    direct=request("/api/v1/production-deviations/"+entry["id"])
    assert direct["id"]==entry["id"] and direct["responsibleCode"]=="SHIFT_LEAD",direct
    request("/api/v1/production-deviations/search?pageSize=101&page=1",status=400)
    request("/api/v1/production-deviations/search?status=99&page=1&pageSize=20",status=400)
    request("/api/v1/production-deviations/search?page=0&pageSize=20",status=400)

    # Inactive categories cannot be selected for new records; prior reporting
    # and master labels remain available without rewriting old deviation codes.
    category4=next(x for x in categories if x["code"]=="4")
    request(prefix+"/"+category4["id"],"PUT",{
        "label":category4["label"],"sortOrder":category4["sortOrder"],
        "isActive":False})
    request("/api/v1/production-deviations","POST",{
        "mpsLineId":mps_line_id,"category":4,
        "description":"CI blocked because master category is inactive"},409)
    assert any(x["id"]==entry["id"] for x in request(
        f"/api/v1/production-deviations/search?category=4&page=1&pageSize=100")["rows"])
    request(prefix+"/"+category4["id"],"PUT",{
        "label":category4["label"],"sortOrder":category4["sortOrder"],
        "isActive":True})

    # Existing CAPA keeps the historical display label and stable reporting
    # code; deactivated operational ownership blocks future selection.
    request(prefix+"/"+owner["id"],"PUT",{
        "label":"عنوان جدید برای اقدامات آینده","sortOrder":5,
        "isActive":False})
    active=request(prefix+"?kind=ActionOwner")
    assert "SHIFT_LEAD" not in {x["code"] for x in active},active
    full=request(prefix+"?kind=ActionOwner&includeInactive=true")
    assert any(x["code"]=="SHIFT_LEAD" and not x["isActive"] for x in full),full
    row=next(x for x in request("/api/v1/production-deviations") if x["id"]==entry["id"])
    assert row["responsible"]=="سرپرست شیفت تولید" and row["responsibleCode"]=="SHIFT_LEAD",row
    new_action={
        "rootCause":"Capacity correction verified",
        "correctiveAction":"Reallocated fixture capacity in next production shift",
        "responsible":"ignored when a code is specified",
        "responsibleCode":"SHIFT_LEAD",
        "rootCauseCategoryCode":"CAPACITY",
        "actionDueJalali":jalali,
        "changeReason":"Structured reporting owner changed after correction"
    }
    request(path+"/revise-action","POST",new_action,409)
    revised=request(path+"/revise-action","POST",{
        **new_action,"responsibleCode":"QA_TEAM"})
    assert revised["status"]==1,revised
    row=next(x for x in request("/api/v1/production-deviations") if x["id"]==entry["id"])
    assert row["responsibleCode"]=="QA_TEAM",row
    assert any("REVISION:OldOwnerCode:" in e["details"] for e in row["events"]),row
    assert row["responsible"]=="گروه کنترل کیفیت",row

    # Rename must not affect earlier accountable historical action snapshots.
    request(prefix+"/"+owner["id"],"PUT",{
        "label":"سرپرست شیفت تولید","sortOrder":5,"isActive":True})
    print("PASS: SQL reference catalog, immutable codes, live/inactive choices, classified CAPA changes, additive audit and server-side paged filters.")
