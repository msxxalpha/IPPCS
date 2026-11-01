"""Stage36: eight analytical modules have actual replayable trial paths.
Runs on disposable CI SQL with real prior stage fixtures. No external ERP
connections and no factory acceptance is inferred.
"""

def stage36_acceptance(request, jalali):
    base="/api/v1/factory-analytical-verification"
    state=request(base)
    assert state["type"]=="DevelopmentAnalyticalVerification",state
    assert state["releaseAuthorized"] is False
    assert state["factoryAccepted"]==0
    assert state["targetModules"]==[2,3,5,6,7,14,16,17]
    assert [g["number"] for g in state["modules"]]==state["targetModules"]
    assert len(state["modules"])==8
    for g in state["modules"]:
        assert g["page"].startswith("/"),g
        assert g["liveTestRoute"],g
        assert g["checks"],g
        assert g["state"] in ("MissingInputs","ReadyForFirstTest","EvidenceAvailable"),g
        assert g["canRun"]==all(c["ready"] for c in g["checks"]),g
        assert g["hasRecordedEvidence"]==(g["evidenceCount"]>0),g
    items={x["number"]:x for x in state["modules"]}
    assert items[5]["hasRecordedEvidence"],items[5]
    assert items[6]["canRun"],items[6]
    assert items[7]["hasRecordedEvidence"],items[7]
    assert items[14]["canRun"],items[14]
    assert items[16]["canRun"],items[16]
    assert state["examples"]["approvedMpsId"],state
    assert state["examples"]["finishedItemId"],state
    assert state["examples"]["recentMrpRunId"],state
    assert state["examples"]["recentApsRunId"],state
    assert state["examples"]["recentOrderId"],state
    assert len(state["examples"]["closedMonthJalali"])==10,state
    assert len(state["examples"]["todayJalali"])==10,state

    product=state["examples"]["finishedItemId"]
    forecast=request(f"/api/v1/factory-demand/forecast/{product}"+
                     f"?fromJalali={jalali}&lookback=12&months=1")
    assert len(forecast["rows"])==1,forecast
    assert forecast["historyComplete"]==(len(forecast["missingHistoryMonths"])==0),forecast
    assert forecast["historyMonthsWithRecords"]+len(forecast["missingHistoryMonths"])==12,forecast
    assert forecast["historySamples"]>=forecast["historyMonthsWithRecords"],forecast

    mps=state["examples"]["approvedMpsId"]
    crp=request(f"/api/v1/crp/{mps}/preview")
    assert crp["mode"]=="RoughCutSingleDay" and isinstance(crp["rows"],list),crp
    mrp=request("/api/v1/mrp/runs/"+state["examples"]["recentMrpRunId"])
    assert mrp["mpsVersionId"] and mrp["results"],mrp
    aps=request("/api/v1/factory-aps/runs/"+state["examples"]["recentApsRunId"])
    assert aps["result"],aps
    sop=request("/api/v1/factory-sop")
    assert sop and sop[0]["id"],sop
    oee=request("/api/v1/oee/clock-verified?dateJalali="+jalali)
    assert oee["timeZone"]=="Asia/Tehran" and isinstance(oee["rows"],list),oee
    cost=request("/api/v1/factory-cost/orders/"+
                 state["examples"]["recentOrderId"]+"/actual-variance")
    assert isinstance(cost["rows"],list),cost

    before={g["number"]:g["evidenceCount"] for g in state["modules"]}
    result=request("/api/v1/factory-whatif/capacity","POST",{
        "mpsVersionId":mps,"startJalali":jalali,
        "horizonDays":14,"baselineCapacity":1,"scenarioCapacity":0.8})
    assert "baseline" in result and "scenario" in result,result
    after=request(base)
    assert {g["number"]:g["evidenceCount"] for g in after["modules"]}==before,after
    assert after["factoryAccepted"]==0 and not after["releaseAuthorized"]
    print("PASS: eight analytical readiness checks, live MRP/CRP/APS/SOP/OEE/cost/What-if calls, zero mutated snapshots or fabricated factory UAT.")
