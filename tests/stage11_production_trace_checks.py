def stage11_acceptance(request, component_id, center_id, jalali):
    def new_item(code,kind):
        return request("/api/v1/items","POST",{"code":code,
          "name":code,"unit":"PCS","type":kind},201)
    semi=new_item("CI-ST11-SEMIFINISHED",1)
    finished=new_item("CI-ST11-FINAL",2)
    def route_bom(item,material,count,prefix):
        route=request("/api/v1/routings","POST",{
          "itemId":item,"revisionCode":prefix+"-ROUTE",
          "effectiveFromJalali":jalali,"effectiveToJalali":None},201)
        request(f"/api/v1/routings/{route['id']}/operations","POST",{
          "sequence":10,"name":"Finish output",
          "workCenterId":center_id,"setupMinutes":1,
          "runMinutesPerUnit":1,"yieldPercent":100},201)
        request(f"/api/v1/routings/{route['id']}/approve","POST")
        bom=request("/api/v1/bom-revisions","POST",{
          "parentItemId":item,"revision":prefix+"-BOM",
          "validFromJalali":jalali,"validUntilJalali":None},201)
        request(f"/api/v1/bom-revisions/{bom['id']}/lines","POST",{
          "componentItemId":material,"quantityPer":count,"scrapPercent":0,
          "sequence":10},201)
        request(f"/api/v1/bom-revisions/{bom['id']}/approve","POST")
    route_bom(semi["id"],component_id,1,"CI-SEMI11")
    route_bom(finished["id"],semi["id"],2,"CI-FINAL11")
    def completed(item,code,units,lot):
        order=request("/api/v1/manufacturing-orders","POST",{
          "number":code,"itemId":item,"plannedQuantity":units,
          "plannedStartJalali":jalali,"dueOnJalali":jalali},201)
        released=request(f"/api/v1/manufacturing-orders/{order['id']}/release","POST")
        op=released["operations"][0]
        output=request(f"/api/v1/manufacturing-orders/{order['id']}/operations/{op['id']}/confirm",
          "POST",{"goodQuantity":units,"scrapQuantity":0,"reworkQuantity":0,
          "lotCode":lot})
        assert output["status"]==4,output
        confirmations=request(f"/api/v1/manufacturing-orders/{order['id']}/confirmations")
        return order,confirmations[0]
    semi_order,source=completed(semi["id"],"CI-SEMIO11",4,"CI-SEMI-BATCH11")
    final_order,target=completed(finished["id"],"CI-FINALO11",2,"CI-FINAL-BATCH11")
    path=f"/api/v1/manufacturing-orders/{final_order['id']}/assembly-genealogy"
    previous=request(path)
    assert previous["coverage"][0]["missingProductionQuantity"]==4
    claim={"semiFinishedConfirmationId":source["id"],
           "assemblyConfirmationId":target["id"],"componentQuantity":4}
    request(path,"POST",claim,201)
    request(path,"POST",claim,409)
    report=request(path)
    assert len(report["links"])==1
    assert report["coverage"][0]["linkedProductionQuantity"]==4
    assert report["coverage"][0]["missingProductionQuantity"]==0
    for order in (semi_order,final_order):
        res=request(f"/api/v1/manufacturing-orders/{order['id']}/production-readiness")
        assert res["isFinalQualityReady"],res
        qc=request(f"/api/v1/manufacturing-orders/{order['id']}/inspection-summary")
        assert qc["resultsCount"]==0
    assert request(f"/api/v1/manufacturing-orders/{semi_order['id']}/assembly-genealogy",
                   status=409)["message"]
    print("PASS: SQL semi-finished to assembly genealogy, BOM coverage and QC readiness.")

    # Stage 12 uses the already-created disposable SQL fixtures (no ERP writes).
    import subprocess
    tomorrow=subprocess.check_output(["node","-e",
      "let p=new Intl.DateTimeFormat('en-US-u-ca-persian-nu-latn',{timeZone:'Asia/Tehran',year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date(Date.now()+86400000));console.log([p.find(x=>x.type==='year').value,p.find(x=>x.type==='month').value,p.find(x=>x.type==='day').value].join('/'))"
    ],text=True).strip()
    orders=request("/api/v1/manufacturing-orders")
    material=next(x for x in orders if x["number"]=="CI-WIP-MATERIAL-STAGE7")
    controlled=next(x for x in orders if x["number"]=="CI-CONTROLLED-STAGE6")
    prior=request("/api/v1/production-deviations")
    mps_line=next(x for x in prior if x["status"]==2)["mpsLineId"]
    from stage12_factory_insights_checks import stage12_acceptance
    stage12_acceptance(request,jalali,tomorrow,controlled["itemId"],None,
                       component_id,material["id"],controlled["id"],
                       source["id"],target["id"],mps_line)

    from stage13_planning_bridge_checks import stage13_acceptance
    version=next(x for x in request("/api/v1/mps") if x["code"]=="CI-MPS-STAGE7")
    sop=next(x for x in request("/api/v1/factory-sop") if x["code"]=="CI-SOP-001")
    stage13_acceptance(request,jalali,version["id"],sop["id"],center_id,component_id)

    from stage14_factory_governance_checks import stage14_acceptance
    stage14_acceptance(request,jalali,tomorrow,semi["id"],component_id,center_id)

    from stage15_line_side_deviation_checks import stage15_acceptance
    stage15_acceptance(request,jalali,tomorrow,component_id,mps_line)
