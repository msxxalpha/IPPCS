"""Stage 23: disposable SQL role-separated CAPA effectiveness lifecycle.
Creates only local IPPCS test accounts on a disposable SQL Server database.
"""
import http.cookiejar
import json
import urllib.error
import urllib.request

def stage23_acceptance(request, base, mps_line):
    deviation=next(x for x in request("/api/v1/production-deviations")
        if x["description"]=="CI-ST15 capacity corrective action with escalation")
    path=f"/api/v1/production-deviations/{deviation['id']}"
    pending=request("/api/v1/production-deviations/effectiveness-pending")
    assert any(x["id"]==deviation["id"] for x in pending["rows"]),pending
    initial=request(path+"/effectiveness")
    assert initial["state"]=="AwaitingIndependentReview",initial
    assert not initial["independentlyVerified"],initial

    def make_qa(suffix):
        username=f"ci-stage23-qa-{suffix}"
        password="Ci_Stage23_Qa_2026!Secret"
        created=request("/api/v1/system-users","POST",{
            "username":username,"password":password,"role":"QualityInspector"},201)
        assert created["role"]=="QualityInspector"
        jar=http.cookiejar.CookieJar()
        client=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
        def invoke(path,method="GET",data=None,status=200,token=None):
            headers={"Content-Type":"application/json"} if data is not None else {}
            if token is not None:headers["X-IPPCS-CSRF"]=token
            msg=urllib.request.Request(base+path,method=method,headers=headers,
                data=json.dumps(data).encode("utf-8") if data is not None else None)
            try:
                with client.open(msg,timeout=30) as res:
                    code=res.status;raw=res.read()
            except urllib.error.HTTPError as ex:
                code=ex.code;raw=ex.read()
            assert code==status,(method,path,code,status,raw[:500])
            return json.loads(raw) if raw else None
        invoke("/auth/login","POST",{"username":username,"password":password})
        csrf=invoke("/auth/csrf")["token"]
        def qa(path,method="GET",data=None,status=200):
            return invoke(path,method,data,status,csrf if method!="GET" else None)
        return qa

    qa1=make_qa("one")
    qa2=make_qa("two")
    qa1("/api/v1/mps","POST",{"code":"FORBIDDEN-QUALITY-MPS"},403)
    request(path+"/effectiveness/review","POST",{
        "effective":True,"evidenceReference":"QA-CI-SELF",
        "observation":"Planner cannot independently certify own closure."},409)
    passed=qa1(path+"/effectiveness/review","POST",{
        "effective":True,"evidenceReference":"QMS-ST23-PASS",
        "observation":"Independent quality reviewed documented repeated-cycle evidence."})
    assert passed["independentlyVerified"] and not passed["reopenedForCorrection"],passed
    qa2(path+"/effectiveness/review","POST",{
        "effective":True,"evidenceReference":"QMS-ST23-DUP",
        "observation":"Repeated acceptance must be prevented by the audit."},409)
    certified=request(path+"/effectiveness")
    assert certified["state"]=="Verified" and certified["reviewedBy"]=="ci-stage23-qa-one",certified
    assert not any(x["id"]==deviation["id"] for x in
        request("/api/v1/production-deviations/effectiveness-pending")["rows"])
    print("PASS: SQL local quality role independently verifies closed CAPA, self-signature blocked, no duplicate approval.")

    changed=request("/api/v1/production-deviations","POST",{
        "mpsLineId":mps_line,"category":4,
        "description":"CI-ST23 effectiveness rejected and reopened"},201)
    cid=changed["id"];case=f"/api/v1/production-deviations/{cid}"
    action={"rootCause":"Incorrect fixture clamping",
        "correctiveAction":"Recalibrate jig fixture; verify repeatability",
        "responsible":"shop floor owner", "actionDueJalali":"1405/08/05"}
    request(case+"/action","POST",action)
    request(case+"/close","POST",{
        "verification":"CI initial closure evidence requires separate validation"})
    rejected=qa1(case+"/effectiveness/review","POST",{
        "effective":False,"evidenceReference":"QMS-ST23-REJECT",
        "observation":"Reinspection showed recurring fixture slippage; correction is required."})
    assert rejected["status"]==1 and rejected["reopenedForCorrection"],rejected
    assert request(case+"/effectiveness")["state"]=="ReopenedForCorrection"
    request(case+"/effectiveness/review","POST",{
        "effective":True,"evidenceReference":"QMS-ST23-INVALID",
        "observation":"Reopened cases cannot receive approval."},409)
    revised=request(case+"/revise-action","POST",{
        **action,"correctiveAction":"Replace jig clamp and verify consecutive batches",
        "changeReason":"QA effectiveness rejection and repeatability fault"})
    assert revised["status"]==1,revised
    request(case+"/close","POST",{
        "verification":"CI repeat-run samples reviewed after correcting the fixture"})
    assert request(case+"/effectiveness")["state"]=="AwaitingIndependentReview"
    qa1(case+"/effectiveness/review","POST",{
        "effective":True,"evidenceReference":"QMS-ST23-SAME-QA",
        "observation":"Reviewer of failed check cannot independently approve the rerun."},409)
    accepted=qa2(case+"/effectiveness/review","POST",{
        "effective":True,"evidenceReference":"QMS-ST23-RERUN",
        "observation":"New independent acceptance follows corrected line observations."})
    assert accepted["independentlyVerified"],accepted
    final=request(case+"/effectiveness")
    assert final["state"]=="Verified" and final["reviewedBy"]=="ci-stage23-qa-two",final
    report=next(x for x in request("/api/v1/production-deviations") if x["id"]==cid)
    assert report["effectiveness"]["independentlyVerified"]
    assert sum(e["details"].startswith("EFFECTIVENESS_V1:")
        for e in report["events"])==2
    print("PASS: SQL reject CAPA effectiveness, reopen, revise with immutable events, re-close, independent second QA verification.")
