"""Stage 22: authenticated, disposable SQL Server engineering changeover end-to-end.
Only IPPCS planning and execution data, no ERP system integration.
"""

def stage22_acceptance(request, jalali, tomorrow, component_id):
    import http.cookiejar
    import json
    import urllib.error
    import urllib.request
    api_base="http://127.0.0.1:5295"
    username="ci-stage32-quality-reviewer"
    password="Ci_Stage32_Quality!2026"
    request("/api/v1/system-users","POST",{
      "username":username,"password":password,"role":"QualityInspector"},201)
    qa_client=urllib.request.build_opener(
      urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    def qa_call(path,method="GET",payload=None,expected=200,token=None):
        headers={"Content-Type":"application/json"} if payload is not None else {}
        if token is not None:headers["X-IPPCS-CSRF"]=token
        req=urllib.request.Request(api_base+path,method=method,headers=headers,
          data=json.dumps(payload).encode("utf-8") if payload is not None else None)
        try:
            with qa_client.open(req,timeout=30) as response:
                code=response.status;raw=response.read()
        except urllib.error.HTTPError as error:
            code=error.code;raw=error.read()
        assert code==expected,(path,code,expected,raw[:500])
        return json.loads(raw) if raw else None
    qa_call("/auth/login","POST",{"username":username,"password":password})
    qa_csrf=qa_call("/auth/csrf")["token"]
    def qa(path,payload,status=200):
        return qa_call(path,"POST",payload,status,qa_csrf)

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
    control_old=request("/api/v1/control-plans","POST",{
        "routingRevisionId":route_old["id"],"code":"CI-ST22-ROUTE-A-CONTROL",
        "validFromJalali":jalali,"validUntilJalali":None},201)
    request(f"/api/v1/control-plans/{control_old['id']}/characteristics","POST",{
        "operationSequence":10,"code":"ST22-OLD-FINAL-CHECK",
        "description":"Old routing quality check","unit":"N",
        "minimum":0,"maximum":100,"mandatory":True},201)
    request(f"/api/v1/control-plans/{control_old['id']}/approve","POST")
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
    request("/api/v1/factory-engineering/cutover","POST",
            {**base,"dryRun":False},409)
    prepared=request("/api/v1/factory-engineering/eco-approvals","POST",base,201)
    assert prepared["status"]==0,prepared
    approval=f"/api/v1/factory-engineering/eco-approvals/{prepared['id']}"
    request("/api/v1/factory-engineering/eco-approvals","POST",base,409)
    request(approval+"/quality-review","POST",{
      "approve":True,"evidenceReference":"QA-ST32-SELF",
      "observation":"Engineering submitter cannot approve own request."},409)
    reviewed=qa(approval+"/quality-review",{
      "approve":True,"evidenceReference":"QA-ST32-ROUTING",
      "observation":"Independent QA checked new jig and effective date."})
    assert reviewed["approved"] and reviewed["status"]==1,reviewed
    qa(approval+"/quality-review",{
      "approve":True,"evidenceReference":"QA-ST32-DUP",
      "observation":"Duplicate independent review cannot be saved."},409)
    applied=request("/api/v1/factory-engineering/cutover","POST",
                    {**base,"dryRun":False})
    assert applied["ecoApprovalStatus"]=="Executed",applied
    assert applied["journalId"],applied
    control_new=request("/api/v1/control-plans","POST",{
        "routingRevisionId":route_new["id"],"code":"CI-ST22-ROUTE-B-CONTROL",
        "validFromJalali":tomorrow,"validUntilJalali":None},201)
    request(f"/api/v1/control-plans/{control_new['id']}/characteristics","POST",{
        "operationSequence":10,"code":"ST22-NEW-FINAL-CHECK",
        "description":"New routing quality check","unit":"N",
        "minimum":0,"maximum":100,"mandatory":True},201)
    request(f"/api/v1/control-plans/{control_new['id']}/approve","POST")
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
    # Approval references are immutable business keys across the full ECO journal,
    # not merely per BOM or routing revision.
    request("/api/v1/factory-engineering/cutover","POST",
            {**bom_change,"approvalReference":"ECO-CI-ST22-ROUTING",
             "dryRun":True},409)
    bom_preview=request("/api/v1/factory-engineering/cutover","POST",
                        {**bom_change,"dryRun":True})
    assert bom_preview["protectedFrozenOrders"]>=2,bom_preview
    request("/api/v1/factory-engineering/cutover","POST",
            {**bom_change,"dryRun":False},409)
    bom_eco=request("/api/v1/factory-engineering/eco-approvals","POST",bom_change,201)
    qa(f"/api/v1/factory-engineering/eco-approvals/{bom_eco['id']}/quality-review",{
      "approve":True,"evidenceReference":"QA-ST32-BOM",
      "observation":"Independent QA checked coefficient change and dated ECO."})
    committed=request("/api/v1/factory-engineering/cutover","POST",
                      {**bom_change,"dryRun":False})
    assert committed["ecoApprovalStatus"]=="Executed",committed
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
    eco_history=request(f"/api/v1/factory-engineering/eco-approvals?itemId={item['id']}")
    assert len(eco_history)==2 and all(x["status"]==3 and
        x["requestedBy"]=="ci-admin" and
        x["qualityReviewedBy"]==username and x["executedUtc"] for x in eco_history),eco_history
    print("PASS: SQL two-account independent ECO quality review, unique references and signed exact-version cutover.")
    print("PASS: SQL BOM effective cutover and draft rebind preserve previously released revision linkage.")
