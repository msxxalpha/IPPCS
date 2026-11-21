using Xunit;

public sealed class MrpDraftOrderConversionTests
{
    [Fact]
    public void Stable_order_number_is_scoped_to_saved_run_and_lot()
    {
        var run = Guid.NewGuid();
        var number = MrpDraftOrderConversion.OrderNumber(run, "PLAN:ITEM:20261101");
        Assert.Equal(number, MrpDraftOrderConversion.OrderNumber(run, "PLAN:ITEM:20261101"));
        Assert.NotEqual(number, MrpDraftOrderConversion.OrderNumber(Guid.NewGuid(), "PLAN:ITEM:20261101"));
        Assert.StartsWith("MRP-", number);
        Assert.True(number.Length <= 64);
    }

    [Fact]
    public void External_proposals_are_never_convertible()
    {
        var lot = new NetPegPlannedLot("PLAN:RAW:20261101", Guid.NewGuid(),
            new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 31), 10, 10, 0,
            "ExternalPlanningOnly", null, false, 0);
        var graph = new NetPeggingReport([], [], [lot]);
        Assert.False(MrpDraftOrderConversion.TryBuildEvidence(Guid.NewGuid(), lot,
            graph, out var evidence, out var error));
        Assert.Null(evidence);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Subassembly_source_retains_multiple_parent_lots_without_fake_mps_link()
    {
        var run = Guid.NewGuid();
        var item = Guid.NewGuid();
        var parentA = "PLAN:PARENT-A:20261105";
        var parentB = "PLAN:PARENT-B:20261106";
        var lot = new NetPegPlannedLot("PLAN:SEMI:20261104", item,
            new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 2), 20, 20, 0,
            "Manufacturing", Guid.NewGuid(), false, 0);
        var graph = new NetPeggingReport(
            [
                new NetPegDemand("D-A", item, lot.ReceiptOn, 8, null, parentA, Guid.NewGuid(), "A", 2),
                new NetPegDemand("D-B", item, lot.ReceiptOn, 12, null, parentB, Guid.NewGuid(), "B", 2)
            ],
            [
                new NetPegAllocation("D-A", item, lot.ReceiptOn, "ProposedManufacturingReceipt",
                    lot.LotKey, lot.ReceiptOn, 8, false),
                new NetPegAllocation("D-B", item, lot.ReceiptOn, "ProposedManufacturingReceipt",
                    lot.LotKey, lot.ReceiptOn, 12, false)
            ], [lot]);

        Assert.True(MrpDraftOrderConversion.TryBuildEvidence(run, lot, graph,
            out var evidence, out var error), error);
        Assert.NotNull(evidence);
        Assert.Empty(evidence.DirectMpsLinks);
        Assert.Equal(new[] { parentA, parentB }, evidence.ParentLotKeys);
        Assert.Equal(20m, evidence.Demands.Sum(x => x.Quantity));
    }

    [Fact]
    public void Direct_mps_source_is_aggregated_by_real_mps_line()
    {
        var item = Guid.NewGuid();
        var line = Guid.NewGuid();
        var lot = new NetPegPlannedLot("PLAN:FG:20261105", item,
            new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 3), 15, 15, 0,
            "Manufacturing", Guid.NewGuid(), false, 0);
        var graph = new NetPeggingReport(
            [new NetPegDemand("MPS:" + line.ToString("N"), item, lot.ReceiptOn,
                15, line, null, null, "FG", 0)],
            [new NetPegAllocation("MPS:" + line.ToString("N"), item, lot.ReceiptOn,
                "ProposedManufacturingReceipt", lot.LotKey, lot.ReceiptOn, 15, false)],
            [lot]);
        Assert.True(MrpDraftOrderConversion.TryBuildEvidence(Guid.NewGuid(), lot,
            graph, out var evidence, out var error), error);
        var link = Assert.Single(evidence!.DirectMpsLinks);
        Assert.Equal(line, link.MpsLineId);
        Assert.Equal(15m, link.Quantity);
    }

    [Fact]
    public void Parent_lot_resolves_all_manufacturing_child_lots_and_total_allocated_quantity()
    {
        var item = Guid.NewGuid();
        var parent = "PLAN:FG:20261110";
        var otherParent = "PLAN:FG:20261111";
        var childLotKey = "PLAN:SEMI:20261108";
        var day = new DateOnly(2026, 11, 8);
        var graph = new NetPeggingReport(
            [
                new NetPegDemand("D-PARENT", item, day, 8, null, parent, Guid.NewGuid(), "P", 1),
                new NetPegDemand("D-OTHER", item, day, 4, null, otherParent, Guid.NewGuid(), "O", 1),
                new NetPegDemand("D-EXTERNAL", item, day, 2, null, parent, null, "E", 1)
            ],
            [
                new NetPegAllocation("D-PARENT", item, day, "ProposedManufacturingReceipt",
                    childLotKey, day, 8, false),
                new NetPegAllocation("D-OTHER", item, day, "ProposedManufacturingReceipt",
                    childLotKey, day, 4, false),
                new NetPegAllocation("D-EXTERNAL", item, day, "ProposedExternalReceipt",
                    "EXT:SEMI", day, 2, false)
            ],
            [
                new NetPegPlannedLot(childLotKey, item, day, day.AddDays(-2),
                    15, 12, 3, "Manufacturing", Guid.NewGuid(), false, 0)
            ]);

        var required = Assert.Single(MrpDraftOrderConversion.RequiredChildLots(graph, parent));

        Assert.Equal(childLotKey, required.LotKey);
        Assert.Equal(item, required.ItemId);
        Assert.Equal(12m, required.RequiredAllocatedQuantity);
    }

    [Fact]
    public void Direct_mps_lot_has_no_manufacturing_child_prerequisite()
    {
        var item = Guid.NewGuid();
        var lot = new NetPegPlannedLot("PLAN:FG:20261110", item,
            new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 9),
            5, 5, 0, "Manufacturing", Guid.NewGuid(), false, 0);
        var graph = new NetPeggingReport(
            [new NetPegDemand("MPS-1", item, lot.ReceiptOn, 5, Guid.NewGuid(), null, null, "FG", 0)],
            [new NetPegAllocation("MPS-1", item, lot.ReceiptOn, "ProposedManufacturingReceipt",
                lot.LotKey, lot.ReceiptOn, 5, false)],
            [lot]);

        Assert.Empty(MrpDraftOrderConversion.RequiredChildLots(graph, lot.LotKey));
    }

    [Fact]
    public void Child_order_must_be_completed_quality_released_and_have_enough_usable_output()
    {
        var item = Guid.NewGuid();
        var requirement = new MrpDraftOrderConversion.MrpDraftChildLotRequirement(
            "PLAN:SEMI:20261108", item, 13m);

        Assert.Equal("ChildDraftMissing",
            MrpDraftOrderConversion.EvaluateChildReadiness(requirement, null, false)!.Code);

        var child = new ManufacturingOrder
        {
            Number = "MRP-CHILD",
            ItemId = item,
            PlannedQuantity = 15m,
            Status = ManufacturingOrderStatus.Released,
            Operations =
            [
                new ManufacturingOrderOperation
                {
                    Sequence = 10, IsComplete = true, GoodQuantity = 12m,
                    InspectionRequirements =
                    [new ManufacturingInspectionRequirement { Mandatory = true, LastPassed = true }]
                }
            ]
        };
        Assert.Equal("ChildNotCompleted",
            MrpDraftOrderConversion.EvaluateChildReadiness(requirement, child, false)!.Code);

        child.Status = ManufacturingOrderStatus.Completed;
        Assert.Equal("ChildQualityNotReleased",
            MrpDraftOrderConversion.EvaluateChildReadiness(requirement, child, true)!.Code);

        Assert.Equal("ChildOutputShortage",
            MrpDraftOrderConversion.EvaluateChildReadiness(requirement, child, false)!.Code);

        child.Operations[0].GoodQuantity = 14m;
        child.Operations[0].QualityRejectedQuantity = 2m;
        Assert.Equal("ChildOutputShortage",
            MrpDraftOrderConversion.EvaluateChildReadiness(requirement, child, false)!.Code);

        child.Operations[0].QualityRejectedQuantity = 0m;
        Assert.Null(MrpDraftOrderConversion.EvaluateChildReadiness(requirement, child, false));
    }

    [Fact]
    public void MRP_source_key_parsing_rejects_malformed_or_ambiguous_keys()
    {
        var runId = Guid.NewGuid();
        var sourceKey = MrpDraftOrderConversion.SourceKey(runId, "PLAN:SEMI:20261108");

        Assert.True(MrpDraftOrderConversion.TryParseSourceKey(sourceKey, out var parsedRun, out var lotKey));
        Assert.Equal(runId, parsedRun);
        Assert.Equal("PLAN:SEMI:20261108", lotKey);
        Assert.False(MrpDraftOrderConversion.TryParseSourceKey("bad|PLAN:SEMI", out _, out _));
        Assert.False(MrpDraftOrderConversion.TryParseSourceKey(null, out _, out _));
    }
}
