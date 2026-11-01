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
        Assert.Equal([parentA, parentB], evidence.ParentLotKeys);
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
}
