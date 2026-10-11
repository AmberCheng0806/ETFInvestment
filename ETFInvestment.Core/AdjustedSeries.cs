using ETFInvestment.Core.Models;
namespace ETFInvestment.Core;

public static class AdjustedSeries
{
    /// <summary>
    /// Calculates the factor from pre-event close and an official reference price.
    /// Confirm event metadata and avoid accidentally applying both a dividend and a reference factor.
    /// </summary>
    public static AdjustmentEvent FromReferencePrice(DateOnly exDate, decimal preEventClose, decimal referencePrice,
        AdjustmentKind kind, string source)
    {
        if (preEventClose <= 0m || referencePrice <= 0m)
            throw new ArgumentOutOfRangeException(nameof(referencePrice), "Prices must be positive.");
        return new AdjustmentEvent(exDate, referencePrice / preEventClose, kind, source);
    }

    public static IReadOnlyList<EtfPrice> Apply(IReadOnlyList<EtfPrice> prices, IReadOnlyList<AdjustmentEvent> events)
    {
        ValidatePrices(prices);
        foreach (var ev in events)
            if (ev.Factor <= 0m || ev.Factor > 100m || string.IsNullOrWhiteSpace(ev.Source))
                throw new ArgumentException("Unconfirmed or invalid adjustment event. Review manually.", nameof(events));
        if (events.Select(e => e.ExDate).Distinct().Count() != events.Count)
            throw new ArgumentException("Multiple adjustment events on one date require a single confirmed combined factor.", nameof(events));

        // O(n*m) is deliberate for <= several thousand daily candles and a few dozen events.
        return prices.Select(p => p with
        {
            Close = p.Close * events.Where(e => p.Date < e.ExDate).Aggregate(1m, (f, e) => f * e.Factor)
        }).ToArray();
    }

    public static void ValidatePrices(IReadOnlyList<EtfPrice> prices)
    {
        for (int i = 0; i < prices.Count; i++)
        {
            if (prices[i].Close <= 0m || string.IsNullOrWhiteSpace(prices[i].Symbol))
                throw new ArgumentException("Market prices must have valid symbol and positive close.");
            if (i > 0 && (prices[i - 1].Date >= prices[i].Date || prices[i - 1].Symbol != prices[i].Symbol))
                throw new ArgumentException("Market prices must be sorted, unique and of one symbol.");
        }
    }
}
