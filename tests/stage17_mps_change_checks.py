def stage17_acceptance(request,mps_id):
    all_mps=request("/api/v1/mps")
    parent=next(x for x in all_mps if x["id"]==mps_id)
    assert parent["state"]==1 and parent["lines"],parent
    line=parent["lines"][0]
    original_rows=[(x["itemId"],x["quantity"],x["neededOnJalali"],x["reference"])
                   for x in parent["lines"]]

    clone=request(f"/api/v1/mps/{mps_id}/controlled-revision","POST",{
      "code":"CI-ST17-MPS-REVISION",
      "reason":"CI approved program changed after documented capacity discussion"},201)
    child=clone["id"]
    assert clone["sourceVersionId"]==mps_id and clone["clonedLines"]==len(original_rows),clone
    request(f"/api/v1/mps/{mps_id}/controlled-revision","POST",{
      "code":"CI-ST17-DUPLICATE-PENDING",
      "reason":"must reject independent competing draft from identical parent"},409)
    plans=request("/api/v1/mps")
    parent_after=next(x for x in plans if x["id"]==mps_id)
    assert parent_after["state"]==1
    assert [(x["itemId"],x["quantity"],x["neededOnJalali"],x["reference"])
            for x in parent_after["lines"]]==original_rows
    child_plan=next(x for x in plans if x["id"]==child)
    assert child_plan["state"]==0 and len(child_plan["lines"])==len(original_rows)
    assert child_plan["lines"][0]["id"]!=line["id"]
    evidence=request(f"/api/v1/mps/{child}/change-evidence")
    assert len(evidence["events"])==1,evidence
    event=evidence["events"][0]
    assert event["algorithmVersion"]=="MPS-CONTROLLED-REVISION-1",event
    assert event["original"]["sourceVersionId"]==mps_id
    assert event["original"]["reason"].startswith("CI approved program changed")
    assert len(event["original"]["originalLines"])==len(original_rows)
    print("PASS: SQL approved MPS clones exact independent Draft with immutable origin and mandatory rationale.")

    report=request("/api/v1/mps/compare-change-impact?sourceId="+mps_id+
      "&candidateId="+child)
    assert report["deltaQuantity"]==0 and not report["demand"],report
    added=request(f"/api/v1/mps/{child}/lines","POST",{
      "itemId":line["itemId"],"neededOnJalali":line["neededOnJalali"],
      "quantity":1,"reference":"CI-ST17-EXTRA-ORDER"},201)
    report=request("/api/v1/mps/compare-change-impact?sourceId="+mps_id+
      "&candidateId="+child)
    assert report["deltaQuantity"]==1,report
    assert any(x["itemId"]==line["itemId"] and x["difference"]==1 for x in report["demand"]),report
    assert report["capacity"] and any(x["deltaMinutes"]>0 for x in report["capacity"]),report
    assert report["materials"] and any(x["grossDifference"]>0 for x in report["materials"]),report
    print("PASS: SQL MPS revision shows before/after demand, rough-cut capacity, and assumed net MRP impacts.")

    request(f"/api/v1/mps/{child}/approve","POST")
    proof=request(f"/api/v1/mps/{child}/change-evidence")
    kinds={x["algorithmVersion"] for x in proof["events"]}
    assert kinds=={"MPS-CONTROLLED-REVISION-1","MPS-APPROVAL-AUDIT-1"},proof
    approved=next(x for x in proof["events"] if x["algorithmVersion"]=="MPS-APPROVAL-AUDIT-1")
    assert approved["original"]["lines"] and approved["original"]["approvedBy"]
    request(f"/api/v1/mps/{child}/lines","POST",{
      "itemId":line["itemId"],"neededOnJalali":line["neededOnJalali"],
      "quantity":2,"reference":"must not change approved"},409)
    request(f"/api/v1/mps/{child}/lines/{added['id']}","DELETE",status=409)
    after=next(x for x in request("/api/v1/mps") if x["id"]==child)
    assert after["state"]==1 and len(after["lines"])==len(original_rows)+1,after
    print("PASS: SQL signed approval snapshot freezes controlled MPS and blocks late writes.")
