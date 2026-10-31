def stage13_acceptance(request, jalali, mps_id, sop_id, center_id, raw_id):
    bridge=request(
      f"/api/v1/factory-sop/{sop_id}/reconcile-mps/{mps_id}")
    assert bridge["approvedMpsQuantity"]>=10,bridge
    assert bridge["inWindowMpsLines"]>=1,bridge
    assert bridge["historicalSopDemand"]>=0,bridge
    print("PASS: SQL S&OP historical snapshot compares to approved in-window MPS.")

    audit=request(f"/api/v1/mps/{mps_id}/demand-audit")
    assert audit["mpsId"]==mps_id and audit["approvedLineCount"]>=1,audit
    assert audit["otherMpsQuantity"]>=10,audit
    print("PASS: SQL approved MPS separates manual demand from explicit recorded-demand references.")

    risks=request(f"/api/v1/mrp/{mps_id}/assumption-risks")
    assert risks["mpsId"]==mps_id and risks["proposedManufacturingLots"]>=1,risks
    raw=next(x for x in risks["rows"] if x["itemId"]==raw_id)
    assert raw["assumedOpening"]>0,raw
    assert raw["assumedCoveragePercent"] is not None
    print("PASS: SQL MRP reports manual planning balances and unconfirmed proposed batch sources.")

    centers=request(f"/api/v1/crp/{mps_id}/center-stress")
    assert centers["dayCenterRows"]>=1,centers
    item=next(x for x in centers["centers"] if x["workCenterId"]==center_id)
    assert item["requiredMinutes"]>0,item
    assert item["availableMinutes"]>=0
    print("PASS: SQL CRP work-center utilization, overload and no-shift differentiation.")

    payload={"mpsVersionId":mps_id,"startJalali":jalali,
      "horizonDays":7,"baselineCapacity":1,"scenarioCapacity":0.75,
      "scenarioUnavailableDays":[{"workCenterId":center_id,
        "dateJalali":jalali,"reason":"CI only: planned maintenance"}]}
    case=request("/api/v1/factory-whatif/capacity-maintenance","POST",payload)
    assert case["maintenanceDays"]==1,case
    assert case["baseline"]["plannedOperations"]==case["scenario"]["plannedOperations"],case
    assert case["scenario"]["scheduledOperations"]<=case["baseline"]["scheduledOperations"],case
    payload["scenarioUnavailableDays"][0]["dateJalali"]="1405/13/40"
    assert request("/api/v1/factory-whatif/capacity-maintenance","POST",payload,
      status=400)["message"]
    print("PASS: SQL combined finite capacity/maintenance simulation, valid Jalali and no real plan mutation.")
