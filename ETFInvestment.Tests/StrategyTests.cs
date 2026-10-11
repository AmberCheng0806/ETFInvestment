using ETFInvestment.Core;
using ETFInvestment.Core.Models;
using ETFInvestment.Core.Strategies;
using Xunit;

namespace ETFInvestment.Tests;
public sealed class StrategyTests
{
    private static EtfPrice[] Prices(string symbol, int n, decimal price = 100m, DateOnly? start = null) =>
        Enumerable.Range(0, n).Select(i => new EtfPrice(symbol, (start ?? new DateOnly(2026, 1, 1)).AddDays(i), price)).ToArray();
    private static CoreEtfState State(string symbol, DateOnly start, DateOnly deadline, decimal spent = 0,
        params TradeRecord[] trades) => new(symbol, 400000, start, deadline, spent, trades);

    [Fact] public void MovingAverage20AndInsufficientHistory()
    {
        Assert.Null(Indicators.SimpleMovingAverage(Prices("0050", 19), 20));
        Assert.Equal(100m, Indicators.SimpleMovingAverage(Prices("0050", 20), 20));
    }

    [Fact] public void DrawdownUsesLast20Closes()
    {
        var p = Prices("0056", 20); p[^1] = p[^1] with { Close = 97m };
        Assert.Equal(0.03m, Indicators.DrawdownFromHigh(p, 20));
    }

    [Fact] public void AdjustmentAppliesOnlyBeforeEventAndSupportsSplit()
    {
        var before = new EtfPrice("0050", new DateOnly(2026, 3, 1), 100m);
        var ex = new EtfPrice("0050", new DateOnly(2026, 3, 2), 50m);
        var adj = AdjustedSeries.Apply([before, ex],
            [AdjustedSeries.FromReferencePrice(ex.Date, 100m, 50m, AdjustmentKind.Split, "manually-confirmed")]);
        Assert.Equal(50m, adj[0].Close);
        Assert.Equal(50m, adj[1].Close);
    }

    [Fact] public void DividendEventDoesNotCreateFalseDrawdown()
    {
        var prices = Prices("0056", 19).Append(new EtfPrice("0056", new DateOnly(2026, 2, 1), 95m)).ToArray();
        var adjusted = AdjustedSeries.Apply(prices,
            [AdjustedSeries.FromReferencePrice(new DateOnly(2026, 2, 1), 100m, 95m, AdjustmentKind.CashDividend, "official-ref")]);
        Assert.Equal(0m, Indicators.DrawdownFromHigh(adjusted, 20));
    }

    [Fact] public void FirstBuyCandidateAtThreePercent()
    {
        var p = Prices("0056", 20); p[^1] = p[^1] with { Close = 97m };
        var s = State("0056", p[0].Date, p[^1].Date.AddMonths(12));
        Assert.Equal(SignalStatus.CandidateBuy, CoreEtfStrategy.Evaluate(s, new CoreEtfRules(0.03m), p).Status);
    }

    [Fact] public void FivePercentFirstThresholdWorks()
    {
        var p = Prices("0050", 20); p[^1] = p[^1] with { Close = 97m };
        Assert.Equal(SignalStatus.Watch, CoreEtfStrategy.Evaluate(
            State("0050", p[0].Date, p[^1].Date.AddMonths(12)), new CoreEtfRules(0.05m), p).Status);
    }

    [Fact] public void FirstWaitExpiresAtTwentySessionsHalfBatch()
    {
        var p = Prices("0056", 22); var s = State("0056", p[0].Date, p[^1].Date.AddMonths(12));
        var signal = CoreEtfStrategy.Evaluate(s, new CoreEtfRules(0.03m), p);
        Assert.Equal(SignalStatus.DueBuy, signal.Status);
        Assert.Equal(1000, signal.SuggestedShares);
    }

    [Fact] public void SubsequentBuyAtFivePercentAndNoLookAhead()
    {
        var p = Prices("0056", 25); p[^1] = p[^1] with { Close = 95m };
        var trade = new TradeRecord("0056", p[^5].Date, 1000, 100m, 0, true);
        var s = State("0056", p[0].Date, p[^1].Date.AddMonths(12), 100000, trade);
        Assert.Equal(SignalStatus.CandidateBuy,
            CoreEtfStrategy.EvaluateSubsequent(s, new CoreEtfRules(0.03m), p, 100m).Status);
    }

    [Fact] public void SubsequentElapsedTwentySessionsTriggersBuy()
    {
        var p = Prices("0056", 43);
        var trade = new TradeRecord("0056", p[20].Date, 1000, 100m, 0, true);
        var s = State("0056", p[0].Date, p[^1].Date.AddMonths(12), 100000, trade);
        Assert.Equal(SignalStatus.DueBuy,
            CoreEtfStrategy.EvaluateSubsequent(s, new CoreEtfRules(0.03m), p, 100m).Status);
    }

    [Fact] public void DeadlineUsesRemainingBudgetEvenPastTwoLotCap()
    {
        var p = Prices("0056", 20);
        var s = State("0056", p[0].Date, p[^1].Date);
        var signal = CoreEtfStrategy.Evaluate(s, new CoreEtfRules(0.03m), p);
        Assert.Equal(SignalStatus.DueBuy, signal.Status);
        Assert.True(signal.SuggestedShares > 2000);
    }

    [Fact] public void InsufficientHistoryProducesAwaitingData()
    {
        var p = Prices("0056", 5);
        Assert.Equal(SignalStatus.AwaitingData, CoreEtfStrategy.Evaluate(
            State("0056", p[0].Date, p[^1].Date.AddMonths(12)), new CoreEtfRules(0.03m), p).Status);
    }

    [Fact] public void RebalanceUpHalfIsSell12500()
    {
        var signal = LeveragedEtfStrategy.Evaluate(new RebalanceState(500m, 50000m, 100m), 150m);
        Assert.Equal(RebalanceAction.Sell, signal.Action);
        Assert.Equal(12500m, signal.TargetAmount);
        Assert.Equal(62500m, signal.NewStockValue);
        Assert.Equal(62500m, signal.NewCash);
    }

    [Fact] public void RebalanceDownHalfIsBuy12500()
    {
        var signal = LeveragedEtfStrategy.Evaluate(new RebalanceState(500m, 50000m, 100m), 50m);
        Assert.Equal(RebalanceAction.Buy, signal.Action);
        Assert.Equal(12500m, signal.TargetAmount);
        Assert.Equal(37500m, signal.NewStockValue);
    }

    [Fact] public void LeveragedSignalIsNotATrade()
    {
        var state = new RebalanceState(500m, 50000m, 100m);
        _ = LeveragedEtfStrategy.Evaluate(state, 150m);
        Assert.Equal(100m, state.BasePrice);
        Assert.Equal(500m, state.Units);
    }

    [Fact] public void InvalidOrDuplicateMarketHistoryIsRejected()
    {
        var p = Prices("0050", 2);
        Assert.Throws<ArgumentException>(() => AdjustedSeries.Apply([p[0], p[0]], []));
    }
}
