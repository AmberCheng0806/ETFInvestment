using ETFInvestment.Core.Models;

namespace ETFInvestment.Core;

/// <summary>
/// Mechanical continuity audit. PASS means that cached records are internally consistent,
/// NOT that the source contains all dividends / splits or is approved for trading.
/// </summary>
public static class AdjustedPriceAudit
{
    public sealed record Finding(DateOnly? Date, string Code, string Message);
    public sealed record Report(string Symbol, int PriceCount, int EventCount,
        IReadOnlyList<EtfPrice> AdjustedPrices, IReadOnlyList<Finding> Findings)
    {
        public bool HasBlockingFindings => Findings.Count > 0;
    }

    public static Report Check(string symbol, IReadOnlyList<EtfPrice> prices,
        IReadOnlyList<AdjustmentEvent> events, decimal unexplainedJumpThreshold = 0.35m,
        decimal eventContinuityThreshold = 0.20m)
    {
        if (unexplainedJumpThreshold <= 0m || eventContinuityThreshold <= 0m)
            throw new ArgumentOutOfRangeException(nameof(unexplainedJumpThreshold));
        var findings = new List<Finding>();
        if (prices.Count < 2)
        {
            findings.Add(new(null, "INSUFFICIENT_PRICES", "At least two market days are required."));
            return new(symbol, prices.Count, events.Count, [], findings);
        }
        try { AdjustedSeries.ValidatePrices(prices); }
        catch (ArgumentException ex)
        {
            findings.Add(new(null, "INVALID_PRICE_SERIES", ex.Message));
            return new(symbol, prices.Count, events.Count, [], findings);
        }
        if (prices.Any(p => p.Symbol != symbol))
        {
            findings.Add(new(null, "SYMBOL_MISMATCH", "Prices do not match the requested symbol."));
            return new(symbol, prices.Count, events.Count, [], findings);
        }
        IReadOnlyList<EtfPrice> adjusted;
        try { adjusted = AdjustedSeries.Apply(prices, events); }
        catch (ArgumentException ex)
        {
            findings.Add(new(null, "INVALID_ADJUSTMENT", ex.Message));
            return new(symbol, prices.Count, events.Count, [], findings);
        }
        // Events outside the time window: old events are harmless, future events may be needed
        // when constructing a common adjusted-price base. This audit only covers the cached window.
        foreach (var e in events.Where(e => e.ExDate >= prices[0].Date && e.ExDate <= prices[^1].Date))
        {
            int index = -1;
            for (int i = 0; i < prices.Count; i++)
                if (prices[i].Date >= e.ExDate) { index = i; break; }
            if (index < 1)
                findings.Add(new(e.ExDate, "NO_PRE_EVENT_CLOSE", "Cannot compare an event without pre-event price."));
        }
        for (int i = 1; i < prices.Count; i++)
        {
            DateOnly date = prices[i].Date;
            // A corporate action can take place on a non-trading day; test any event falling
            // between adjacent trade dates, rather than assuming an event date matches a candle.
            var between = events.Where(e => e.ExDate > prices[i-1].Date && e.ExDate <= date).ToArray();
            var adjustedMove = adjusted[i].Close / adjusted[i-1].Close - 1m;
            var rawMove = prices[i].Close / prices[i-1].Close - 1m;
            if (between.Length == 0 && Math.Abs(rawMove) >= unexplainedJumpThreshold)
                findings.Add(new(date, "UNEXPLAINED_LARGE_MOVE", $"Raw move {rawMove:P2} has no known action."));
            if (between.Length > 0 && Math.Abs(adjustedMove) >= eventContinuityThreshold)
                findings.Add(new(date, "EVENT_CONTINUITY", $"Adjusted move {adjustedMove:P2} near known corporate action; verify event factor."));
        }
        return new(symbol, prices.Count, events.Count, adjusted, findings);
    }
}
