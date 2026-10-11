using ETFInvestment.Core;
using ETFInvestment.Core.Models;
using Xunit;

namespace ETFInvestment.Tests;
public sealed class AdjustedPriceAuditTests
{
    [Fact] public void Split22To1DoesNotLookLikeMarketCrash()
    {
        var before = new EtfPrice("00631L", new(2026,3,30), 443.15m);
        var after = new EtfPrice("00631L", new(2026,3,31), 20.14m);
        var e = new AdjustmentEvent(new(2026,3,31), 1m/22m, AdjustmentKind.Split, "TWSE");
        var report = AdjustedPriceAudit.Check("00631L", [before, after], [e]);
        Assert.False(report.HasBlockingFindings);
        Assert.InRange(report.AdjustedPrices[0].Close, 20.13m, 20.15m);
    }
    [Fact] public void MissingSplitBlocksFakeCrash()
    {
        var report = AdjustedPriceAudit.Check("00631L",
          [new("00631L", new(2026,3,30),443.15m),new("00631L",new(2026,3,31),20.14m)], []);
        Assert.Contains(report.Findings,f => f.Code == "UNEXPLAINED_LARGE_MOVE");
    }
    [Fact] public void IncorrectSplitFactorIsFlagged()
    {
        var report = AdjustedPriceAudit.Check("00631L",
          [new("00631L",new(2026,3,30),443.15m),new("00631L",new(2026,3,31),20.14m)],
          [new(new(2026,3,31),0.1m,AdjustmentKind.Split,"incorrect")]);
        Assert.Contains(report.Findings,f => f.Code == "EVENT_CONTINUITY");
    }
    [Fact] public void DividendReferenceFactorMaintainsContinuity()
    {
        var report = AdjustedPriceAudit.Check("0056",
          [new("0056",new(2026,2,2),40m),new("0056",new(2026,2,3),39m)],
          [AdjustedSeries.FromReferencePrice(new(2026,2,3),40m,39m,AdjustmentKind.CashDividend,"Official")]);
        Assert.False(report.HasBlockingFindings);
        Assert.Equal(report.AdjustedPrices[0].Close,report.AdjustedPrices[1].Close);
    }
    [Fact] public void DuplicateEventIsBlocked()
    {
        var ev = new AdjustmentEvent(new(2026,3,31),1m/22m,AdjustmentKind.Split,"TWSE");
        var report = AdjustedPriceAudit.Check("00631L",
          [new("00631L",new(2026,3,30),443.15m),new("00631L",new(2026,3,31),20.14m)], [ev,ev]);
        Assert.Contains(report.Findings,f => f.Code == "INVALID_ADJUSTMENT");
    }
    [Fact] public void OnePriceCannotPass()
    {
        var report = AdjustedPriceAudit.Check("0050",[new("0050",new(2026,1,2),50m)],[]);
        Assert.True(report.HasBlockingFindings);
    }
}
