using ETFInvestment.Core.Models;
namespace ETFInvestment.Core;

public static class Indicators
{
    public static decimal? SimpleMovingAverage(IReadOnlyList<EtfPrice> prices, int period)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        AdjustedSeries.ValidatePrices(prices);
        return prices.Count < period ? null : prices.Skip(prices.Count - period).Average(p => p.Close);
    }

    /// <summary>Uses the preceding N market sessions INCLUDING the current session.</summary>
    public static decimal? DrawdownFromHigh(IReadOnlyList<EtfPrice> prices, int period)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        AdjustedSeries.ValidatePrices(prices);
        if (prices.Count < period) return null;
        decimal high = prices.Skip(prices.Count - period).Max(p => p.Close);
        return 1m - prices[^1].Close / high;
    }

    /// <summary>Counts observed trading sessions strictly AFTER the anchor date.</summary>
    public static int TradingDaysElapsed(IReadOnlyList<EtfPrice> prices, DateOnly anchorDate)
    {
        AdjustedSeries.ValidatePrices(prices);
        return prices.Count(p => p.Date > anchorDate);
    }
}
