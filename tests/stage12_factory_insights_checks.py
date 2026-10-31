def stage12_acceptance(request, today, tomorrow, product_id, route_id,
                       raw_id, material_order_id, final_order_id,
                       first_confirmation, second_confirmation, pegged_line_id):
    trace_forward=request(
      f"/api/v1/manufacturing-genealogy/explore/{first_confirmation}?direction=descendants&depth=8")
    trace_backward=request(
      f"/api/v1/manufacturing-genealogy/explore/{second_confirmation}?direction=ancestors&depth=8")
    assert trace_forward["hasLinks"] and trace_backward["hasLinks"]
    assert len(trace_forward["edges"])>=1
    assert len(trace_backward["nodes"])>=2
    assert request(
      f"/api/v1/manufacturing-genealogy/explore/{first_confirmation}?direction=both&depth=8",
      status=400)["message"]
    print("PASS: authenticated SQL forward/backward lot genealogy reaches internal production links.")

    materials=request(
      f"/api/v1/factory-material/{material_order_id}/execution-reconciliation")
    mr=next(x for x in materials["rows"] if x["componentItemId"]==raw_id)
    assert mr["recordedEntries"]>=1 and mr["actualLoggedConsumption"]>=19,mr
    assert mr["standardForReportedOutput"] is None,mr
    assert mr["outputBasedVariance"] is None,mr
    other=request(f"/api/v1/factory-material/{final_order_id}/execution-reconciliation")
    assert any(x["actualLoggedConsumption"] is None and
               x["standardForReportedOutput"] is not None for x in other["rows"])
    print("PASS: SQL output-scaled line consumption distinguishes missing vs logged observations.")

    # Create a separate controlled production order; no inventory changes.
    spc_order=request("/api/v1/manufacturing-orders","POST",{
      "number":"CI-SPC-STAGE12","itemId":product_id,"plannedQuantity":10,
      "plannedStartJalali":today,"dueOnJalali":today},201)
    released=request(
      f"/api/v1/manufacturing-orders/{spc_order['id']}/release","POST")
    first=released["operations"][0]
    requirements=request(
      f"/api/v1/manufacturing-orders/{spc_order['id']}/inspection-requirements")["requirements"]
    spec=next(x for x in requirements if x["operationSequence"]==10)
    request(
      f"/api/v1/manufacturing-orders/{spc_order['id']}/operations/{first['id']}/confirm",
      "POST",{"goodQuantity":9,"scrapQuantity":1,"reworkQuantity":0,"lotCode":"CI-SPC-12"})
    for n in [10,11,10,12,13]:
        check=request(
          f"/api/v1/manufacturing-orders/{spc_order['id']}/inspections/{spec['id']}",
          "POST",{"value":n,"nonconformingQuantity":0,"note":"Stage12 CI SPC observation"})
        assert check["passed"]
    result=request(f"/api/v1/manufacturing-orders/{spc_order['id']}/spc/{spec['id']}")
    assert result["sampleCount"]==5 and result["upperControlLimit"] is not None,result
    assert result["specificationFailures"]==0,result
    print("PASS: SQL five-sample Control Plan individual chart produces non-null experimental limits.")

    hold=request(f"/api/v1/manufacturing-orders/{spc_order['id']}/quality-holds",
      "POST",{"operationId":first["id"],"quantity":1,
      "reason":"CI open quality investigation"},201)
    assert hold["status"]==0
    request("/api/v1/production-deviations","POST",{
      "mpsLineId":pegged_line_id,"category":5,
      "description":"CI Stage12 unassigned investigation"},201)
    alerts=request("/api/v1/factory-kpis/production-alerts?asOfJalali="+
      tomorrow+"&daysAhead=5")
    assert alerts["criticalCount"]>=1 and alerts["highCount"]>=1,alerts
    assert any(x["type"]=="QualityHold" for x in alerts["alerts"])
    assert any(x["type"]=="DeviationAction" for x in alerts["alerts"])
    assert request("/api/v1/factory-kpis/production-alerts?asOfJalali=1405/13/40&daysAhead=5",
                   status=400)["message"]
    print("PASS: SQL production alerts include QC holds, overdue jobs and unassigned deviations.")
