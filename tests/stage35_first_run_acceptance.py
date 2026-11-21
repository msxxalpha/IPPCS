"""Stage35: realistic first-run readiness + managed factory units.
All operations are scoped to disposable SQL CI. This test never signs off
factory acceptance and never connects to warehouse, purchasing, sales or HR.
"""

def stage35_acceptance(request,route_id,jalali):
    ref="/api/v1/factory-master-options"
    opts=request(ref+"?kind=UnitOfMeasure")
    unit_codes={x["code"] for x in opts}
    assert {"PCS","EA","KG","MM","N","L","BAR","MPA"}.issubset(unit_codes),opts
    units={x["code"]:x for x in opts}
    assert all(x["isActive"] for x in units.values()),opts

    request("/api/v1/items","POST",{
        "code":"CI-ST35-UNKNOWN-UOM","name":"must not create unsupported unit",
        "unit":"WRONG_UOM","type":0},409)
    request("/api/v1/items","POST",{
        "code":"CI-ST35-UNKNOWN-TYPE","name":"must not create unrecognized type",
        "unit":"PCS","type":99},400)

    raw=request("/api/v1/items","POST",{
        "code":"CI-ST35-QA-UNIT-RAW","name":"Unit controlled local item",
        "unit":"kg","type":0},201)
    assert raw["unit"]=="KG",raw
    plan=request("/api/v1/control-plans","POST",{
        "routingRevisionId":route_id,"code":"CI-ST35-UOM-TEST",
        "validFromJalali":jalali,"validUntilJalali":None},201)
    characteristic=f"/api/v1/control-plans/{plan['id']}/characteristics"
    request(characteristic,"POST",{
        "operationSequence":10,"code":"UOM-SOURCE-N",
        "description":"Validated damping test unit",
        "unit":"n","minimum":10,"maximum":100,
        "mandatory":True},201)
    request(characteristic,"POST",{
        "operationSequence":10,"code":"UOM-INVALID-GAUGE",
        "description":"Unsupported measurement unit",
        "unit":"BAD_UNIT","minimum":10,"maximum":100,
        "mandatory":True},409)

    request(ref+"/"+units["L"]["id"],"PUT",{
        "label":units["L"]["label"],"isActive":False,
        "sortOrder":units["L"]["sortOrder"]})
    request("/api/v1/items","POST",{
        "code":"CI-ST35-DISABLED-UOM","name":"Inactive unit should reject",
        "unit":"L","type":0},409)
    assert raw["unit"]=="KG"
    request(ref+"/"+units["L"]["id"],"PUT",{
        "label":units["L"]["label"],"isActive":True,
        "sortOrder":units["L"]["sortOrder"]})

    readiness=request("/api/v1/factory-release-readiness")
    assert readiness["kind"]=="FactoryUatPreparationOnly",readiness
    assert readiness["declaredModules"]==18 and len(readiness["modules"])==18,readiness
    assert [x["number"] for x in readiness["modules"]]==list(range(1,19))
    assert readiness["productionReleaseAuthorized"] is False
    assert readiness["factoryAcceptedModules"]==0
    assert all(x["factoryAcceptance"]=="NotRecorded" and
               not x["productionReleaseAuthorized"] for x in readiness["modules"])
    assert readiness["totalSetupGates"]==9
    checks={x["code"]:x for x in readiness["setup"]}
    assert all(checks[name]["configured"] for name in
               ["Items","Centers","Routing","BOM","Shifts","MPS",
                "DowntimeReasons","HoldReasons"]),checks
    assert 0<readiness["configuredSetupGates"]<=9,readiness
    print("PASS: SQL catalog-driven production/QA units, inactive-unit enforcement and truthful 18-module first-run readiness.")
