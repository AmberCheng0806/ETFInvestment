using ETFInvestment.Core.Models;
namespace ETFInvestment.Core.Strategies;

public static class CoreEtfStrategy
{
    public static BuySignal Evaluate(CoreEtfState state, CoreEtfRules rules, IReadOnlyList<EtfPrice> adjustedPrices)
    {
        ValidateRules(rules);
        if (state.Budget <= 0 || state.Spent < 0 || state.Spent > state.Budget)
            return new BuySignal(SignalStatus.AwaitingData, "Invalid private budget/spent data.", 0);
        if (adjustedPrices.Count == 0 || adjustedPrices.Count < rules.HighLookback)
            return new BuySignal(SignalStatus.AwaitingData, "Insufficient confirmed adjusted-price history.", 0);
        try { AdjustedSeries.ValidatePrices(adjustedPrices); }
        catch (ArgumentException) { return new BuySignal(SignalStatus.AwaitingData, "Invalid price history; manual review required.", 0); }
        if (adjustedPrices[^1].Symbol != state.Symbol)
            return new BuySignal(SignalStatus.AwaitingData, "Symbol mismatch.", 0);
        if (state.Trades.Any(t => t.Symbol != state.Symbol || t.UnitPrice <= 0 || t.Shares <= 0 || t.Date > adjustedPrices[^1].Date))
            return new BuySignal(SignalStatus.AwaitingData, "Invalid or future trade record.", 0);

        decimal remaining = state.Budget - state.Spent;
        if (remaining <= 0) return new BuySignal(SignalStatus.BudgetExhausted, "Budget exhausted.", 0);
        var today = adjustedPrices[^1].Date;
        decimal price = adjustedPrices[^1].Close;
        decimal estimatedUnitCost = price * (1m + rules.FeeRate * rules.FeeMultiplier);
        int affordable = (int)Math.Min(int.MaxValue, decimal.Floor((remaining - rules.MinimumFee) / estimatedUnitCost));
        int normalShares = Math.Min(rules.MaxSharesPerOrder, Math.Max(0, affordable));
        if (normalShares == 0) return new BuySignal(SignalStatus.Watch, "Remaining funds cannot cover 1 share and estimated fees.", 0);

        if (today >= state.FinalDeadline)
            return new BuySignal(SignalStatus.DueBuy, "Final 12-month deadline reached: propose remaining affordable shares.",
                Math.Max(0, affordable), price);

        var buys = state.Trades.Where(t => t.IsBuy).OrderBy(t => t.Date).ToArray();
        if (buys.Length == 0)
        {
            var drawdown = Indicators.DrawdownFromHigh(adjustedPrices, rules.HighLookback);
            if (drawdown >= rules.InitialDrawdown)
                return new BuySignal(SignalStatus.CandidateBuy, "Initial drawdown threshold reached.",
                    Math.Min(normalShares, rules.TargetInitialShares), price);
            if (Indicators.TradingDaysElapsed(adjustedPrices, state.StartDate) >= rules.TradingDayInterval)
            {
                int planned = Math.Max(1, (int)decimal.Ceiling(rules.TargetInitialShares * rules.DueInitialFraction));
                return new BuySignal(SignalStatus.DueBuy, "Initial waiting window expired: half of first planned batch.",
                    Math.Min(normalShares, planned), price);
            }
            return new BuySignal(SignalStatus.Watch, "Waiting for initial drawdown or 20 trading days.", 0, price);
        }

        var last = buys[^1];
        // The recorded execution price must be mapped to TODAY's adjusted price scale by the caller.
        // Without split/dividend-adjusted cost basis, ex-dividend drops can generate false triggers.
        // This field is supplied as an explicit comparator, not inferred from raw trade history.
        return new BuySignal(SignalStatus.AwaitingData,
            "Subsequent buys require an adjusted last-entry comparator: use EvaluateSubsequent(...).", 0, price);
    }

    public static BuySignal EvaluateSubsequent(CoreEtfState state, CoreEtfRules rules,
        IReadOnlyList<EtfPrice> adjustedPrices, decimal adjustedLastBuyPrice)
    {
        ValidateRules(rules);
        if (adjustedLastBuyPrice <= 0) return new BuySignal(SignalStatus.AwaitingData, "Adjusted buy reference missing.", 0);
        if (adjustedPrices.Count < rules.HighLookback)
            return new BuySignal(SignalStatus.AwaitingData, "Insufficient adjusted-price history.", 0);
        try { AdjustedSeries.ValidatePrices(adjustedPrices); }
        catch (ArgumentException) { return new BuySignal(SignalStatus.AwaitingData, "Invalid market history.", 0); }
        if (adjustedPrices[^1].Symbol != state.Symbol) return new BuySignal(SignalStatus.AwaitingData, "Symbol mismatch.", 0);
        if (state.Spent < 0 || state.Budget <= 0 || state.Spent > state.Budget) return new BuySignal(SignalStatus.AwaitingData, "Invalid portfolio state.", 0);
        var last = state.Trades.Where(t => t.IsBuy).OrderBy(t => t.Date).LastOrDefault();
        if (last is null || last.Date > adjustedPrices[^1].Date) return new BuySignal(SignalStatus.AwaitingData, "Missing or future buy record.", 0);
        decimal remaining = state.Budget - state.Spent;
        if (remaining <= 0m) return new BuySignal(SignalStatus.BudgetExhausted, "Budget exhausted.", 0);
        var today = adjustedPrices[^1].Date;
        decimal price = adjustedPrices[^1].Close;
        int affordable = (int)Math.Min(int.MaxValue, decimal.Floor((remaining - rules.MinimumFee) /
            (price * (1m + rules.FeeRate * rules.FeeMultiplier))));
        if (affordable <= 0) return new BuySignal(SignalStatus.Watch, "Cannot afford next share.", 0);
        int quantity = Math.Min(affordable, rules.MaxSharesPerOrder);
        if (today >= state.FinalDeadline)
            return new BuySignal(SignalStatus.DueBuy, "Final deadline reached; propose remaining affordable shares.", affordable, price);
        if (price <= adjustedLastBuyPrice * (1m - rules.NextDrawdown))
            return new BuySignal(SignalStatus.CandidateBuy, "Adjusted price down at least 5% vs last buy.", quantity, price);
        if (Indicators.TradingDaysElapsed(adjustedPrices, last.Date) >= rules.TradingDayInterval)
            return new BuySignal(SignalStatus.DueBuy, "20 trading sessions since last execution.", quantity, price);
        return new BuySignal(SignalStatus.Watch, "Waiting for 5% decline or 20 sessions.", 0, price);
    }

    private static void ValidateRules(CoreEtfRules r)
    {
        if (r.HighLookback < 2 || r.TradingDayInterval < 1 || r.MaxSharesPerOrder < 1 || r.TargetInitialShares < 1 ||
            r.InitialDrawdown < 0 || r.InitialDrawdown >= 1 || r.NextDrawdown < 0 || r.NextDrawdown >= 1 ||
            r.DueInitialFraction <= 0 || r.DueInitialFraction > 1 || r.FeeRate < 0 || r.FeeMultiplier < 0 || r.MinimumFee < 0)
            throw new ArgumentOutOfRangeException(nameof(r), "Invalid rule values.");
    }
}
