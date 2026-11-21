using Xunit;

public sealed class FactoryStageFortyFourPrintContractsTests
{
    [Theory]
    [InlineData(ManufacturingOrderStatus.Draft,"پیش‌نویس")]
    [InlineData(ManufacturingOrderStatus.Released,"آزادشده")]
    [InlineData(ManufacturingOrderStatus.OnHold,"تعلیق")]
    [InlineData(ManufacturingOrderStatus.Cancelled,"لغوشده")]
    public void Source_order_state_is_not_represented_as_signed_approval(
        ManufacturingOrderStatus status,string expected)
    {
        Assert.Contains(expected,FactoryPrintDocuments.WorkOrderState(status));
    }

    [Fact]
    public void Draft_and_approved_planning_have_distinct_print_labels()
    {
        Assert.Contains("پیش‌نویس",FactoryPrintDocuments.ApprovalState(false));
        Assert.Contains("مصوب داخلی",FactoryPrintDocuments.ApprovalState(true));
    }
}
