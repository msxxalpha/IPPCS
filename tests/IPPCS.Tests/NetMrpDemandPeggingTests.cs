using Xunit;

public sealed class NetMrpDemandPeggingTests
{
    private static readonly DateOnly Start = new(2026, 10, 20);

    private static PlanningBalance Balance(Guid item, decimal usable, int leadDays = 0,
        decimal minimum = 1, decimal multiple = 1) => new()
    {
        ItemId = item, SnapshotDate = Start, PhysicalQuantity = usable,
        LeadDays = leadDays, MinimumBatch = minimum, BatchMultiple = multiple
    };

    private static ManufacturingBomRevision Revision(Guid parent, Guid child, decimal per = 1)
    {
        var r = new ManufacturingBomRevision
        {
            ParentItemId = parent, State = ManufacturingRevisionState.Approved,
            Revision = "A", ValidFrom = Start.AddDays(-100)
        };
        r.Lines.Add(new ManufacturingBomLine
        {
            RevisionId = r.Id, ComponentItemId = child, QuantityPer = per, Sequence = 1
        });
        return r;
    }

    private static (List<MrpRow> Mrp, NetPeggingReport Report) Run(
        MpsDemandLine[] lines, ManufacturingBomRevision[] boms,
        PlanningBalance[] balances, PlanningExpectedReceipt[] receipts,
        Dictionary<Guid, PlanningItemType> types)
    {
        var mrp = PlanningCalculators.RunMrp(lines, boms, balances, receipts, types);
        return (mrp, NetMrpDemandPegging.Build(lines, boms, balances, receipts, types, mrp));
    }

    [Fact]
    public void Opening_and_expected_supply_prevent_unnecessary_parent_explosion()
    {
        var finished = Guid.NewGuid(); var raw = Guid.NewGuid();
        var first = new MpsDemandLine { ItemId = finished, Quantity = 6, NeededOn = Start.AddDays(4) };
        var later = new MpsDemandLine { ItemId = finished, Quantity = 4, NeededOn = Start.AddDays(5) };
        var receipt = new PlanningExpectedReceipt { ItemId = finished, AvailableOn = Start.AddDays(5), Quantity = 4 };
        var (_, report) = Run([first, later], [Revision(finished, raw, 2)],
            [Balance(finished, 6), Balance(raw, 0)], [receipt],
            new() { [finished] = PlanningItemType.Finished, [raw] = PlanningItemType.RawMaterial });
        Assert.Empty(report.PlannedLots);
        Assert.DoesNotContain(report.Demands,x => x.ParentLotKey != null);
        Assert.Equal(10m, report.Allocations.Sum(x => x.Quantity));
        Assert.Contains(report.Allocations, x => x.SourceType == "ExpectedReceipt" &&
            x.DemandKey == "MPS:" + later.Id.ToString("N") && x.Quantity == 4m);
    }

    [Fact]
    public void Net_batch_surplus_explodes_complete_parent_lot_and_pegs_to_component()
    {
        var finished = Guid.NewGuid(); var semi = Guid.NewGuid(); var raw = Guid.NewGuid();
        var line = new MpsDemandLine { ItemId = finished, Quantity = 8, NeededOn = Start.AddDays(9) };
        var (_, report) = Run([line], [Revision(finished, semi, 2), Revision(semi, raw, 3)],
            [Balance(finished, 3, 1, 10, 5), Balance(semi, 4, 1), Balance(raw, 0, 0)],
            [], new() { [finished] = PlanningItemType.Finished,
                [semi] = PlanningItemType.SemiFinished, [raw] = PlanningItemType.RawMaterial });
        var rootLot = Assert.Single(report.PlannedLots,x => x.ItemId == finished);
        Assert.Equal(10m, rootLot.Quantity); // 5 net + 5 lot-sizing uplift
        Assert.Equal(5m, rootLot.NetShortage);
        Assert.Equal(5m, rootLot.LotSizingSurplus);
        Assert.Equal(Start.AddDays(8), rootLot.ReleaseOn);
        var semiDemand = Assert.Single(report.Demands,x => x.ParentLotKey == rootLot.LotKey);
        Assert.Equal(20m, semiDemand.Quantity);
        var semiLot = Assert.Single(report.PlannedLots,x => x.ItemId == semi);
        Assert.Equal(16m, semiLot.Quantity);
        Assert.Equal(Start.AddDays(7), semiLot.ReleaseOn);
        var rawNeed = Assert.Single(report.Demands,x => x.ParentLotKey == semiLot.LotKey);
        Assert.Equal(48m, rawNeed.Quantity);
        Assert.Contains(report.Allocations, x => x.DemandKey == rawNeed.DemandKey &&
            x.SourceType == "ProposedExternalReceipt" && !x.IsFirm && x.Quantity == 48m);
        Assert.All(report.Allocations, x => Assert.True(x.AvailableOn <= x.NeededOn));
        Assert.Equal(report.Demands.Sum(x => x.Quantity), report.Allocations.Sum(x => x.Quantity));
    }

    [Fact]
    public void Same_item_across_mps_lines_never_spends_supply_twice()
    {
        var finished = Guid.NewGuid(); var raw = Guid.NewGuid();
        var lineA = new MpsDemandLine { ItemId = finished, Quantity = 5, NeededOn = Start.AddDays(3) };
        var lineB = new MpsDemandLine { ItemId = finished, Quantity = 6, NeededOn = Start.AddDays(3) };
        var (_, report) = Run([lineA, lineB], [Revision(finished, raw)],
            [Balance(finished, 7, 1), Balance(raw, 0)], [],
            new() { [finished] = PlanningItemType.Finished, [raw] = PlanningItemType.RawMaterial });
        Assert.Equal(7m, report.Allocations.Where(x => x.SourceType == "PlanningOpeningBalance" &&
            x.ItemId == finished).Sum(x => x.Quantity));
        Assert.Equal(4m, report.Allocations.Where(x => x.SourceType == "ProposedManufacturingReceipt" &&
            x.ItemId == finished).Sum(x => x.Quantity));
        Assert.Single(report.PlannedLots,x => x.ItemId == finished);
    }

    [Fact]
    public void Batch_surplus_is_reused_for_later_demand_without_duplicate_build_or_component_usage()
    {
        var finished = Guid.NewGuid(); var component = Guid.NewGuid();
        var first = new MpsDemandLine { ItemId = finished, Quantity = 3, NeededOn = Start.AddDays(5) };
        var second = new MpsDemandLine { ItemId = finished, Quantity = 2, NeededOn = Start.AddDays(6) };
        var (_, report) = Run([first, second], [Revision(finished, component)],
            [Balance(finished, 0, 1, 5, 5), Balance(component, 0)], [],
            new() { [finished] = PlanningItemType.Finished,
                    [component] = PlanningItemType.RawMaterial });
        var lot = Assert.Single(report.PlannedLots,x => x.ItemId == finished);
        Assert.Equal(5m, lot.Quantity);
        Assert.Equal(3m, lot.NetShortage);
        Assert.Equal(2m, lot.LotSizingSurplus);
        Assert.Equal(0m, lot.UnallocatedQuantity);
        Assert.Equal(2m, Assert.Single(report.Allocations,x =>
            x.DemandKey == "MPS:" + second.Id.ToString("N")).Quantity);
        var componentNeed = Assert.Single(report.Demands,x => x.ParentLotKey == lot.LotKey);
        Assert.Equal(5m, componentNeed.Quantity);
    }

    [Fact]
    public void Child_need_before_planning_baseline_fails_closed()
    {
        var finished = Guid.NewGuid(); var raw = Guid.NewGuid();
        var line = new MpsDemandLine { ItemId = finished, Quantity = 2, NeededOn = Start.AddDays(1) };
        var boms = new[] { Revision(finished, raw) };
        var balances = new[] { Balance(finished, 0, 3), Balance(raw, 0) };
        var types = new Dictionary<Guid, PlanningItemType>
        {
            [finished] = PlanningItemType.Finished, [raw] = PlanningItemType.RawMaterial
        };
        var rows = PlanningCalculators.RunMrp([line], boms, balances, [], types);
        Assert.Throws<InvalidOperationException>(() =>
            NetMrpDemandPegging.Build([line], boms, balances, [], types, rows));
    }

    [Fact]
    public void Tampered_mrp_row_cannot_create_false_material_allocation()
    {
        var item = Guid.NewGuid();
        var line = new MpsDemandLine { ItemId = item, Quantity = 5, NeededOn = Start.AddDays(1) };
        var balance = Balance(item, 5);
        var types = new Dictionary<Guid, PlanningItemType> { [item] = PlanningItemType.Finished };
        var rows = PlanningCalculators.RunMrp([line], [], [balance], [], types);
        rows[0] = rows[0] with { OpeningAvailable = 6 };
        Assert.Throws<InvalidOperationException>(() =>
            NetMrpDemandPegging.Build([line], [], [balance], [], types, rows));
    }

    [Fact]
    public void Duplicate_mps_line_identity_is_rejected_before_ambiguous_pegging()
    {
        var item = Guid.NewGuid();
        var line = new MpsDemandLine { ItemId = item, Quantity = 5, NeededOn = Start.AddDays(1) };
        var balance = Balance(item, 5);
        var types = new Dictionary<Guid, PlanningItemType> { [item] = PlanningItemType.Finished };
        var rows = PlanningCalculators.RunMrp([line], [], [balance], [], types);
        Assert.Throws<InvalidOperationException>(() =>
            NetMrpDemandPegging.Build([line, line], [], [balance], [], types, rows));
    }

    [Fact]
    public void Duplicate_opening_balance_for_an_item_is_rejected_with_planning_validation()
    {
        var item = Guid.NewGuid();
        var line = new MpsDemandLine { ItemId = item, Quantity = 5, NeededOn = Start.AddDays(1) };
        var balance = Balance(item, 5);
        var types = new Dictionary<Guid, PlanningItemType> { [item] = PlanningItemType.Finished };
        var rows = PlanningCalculators.RunMrp([line], [], [balance], [], types);
        var error = Assert.Throws<InvalidOperationException>(() =>
            NetMrpDemandPegging.Build([line], [], [balance, new PlanningBalance { ItemId = item, SnapshotDate = Start, PhysicalQuantity = 7 }], [], types, rows));
        Assert.Contains("مبنای برنامه‌ریزی", error.Message);
    }

    [Fact]
    public void Duplicate_mrp_item_date_rows_are_rejected_before_allocation()
    {
        var item = Guid.NewGuid();
        var line = new MpsDemandLine { ItemId = item, Quantity = 5, NeededOn = Start.AddDays(1) };
        var balance = Balance(item, 5);
        var types = new Dictionary<Guid, PlanningItemType> { [item] = PlanningItemType.Finished };
        var rows = PlanningCalculators.RunMrp([line], [], [balance], [], types);
        Assert.Throws<InvalidOperationException>(() =>
            NetMrpDemandPegging.Build([line], [], [balance], [], types, [rows[0], rows[0]]));
    }
}
