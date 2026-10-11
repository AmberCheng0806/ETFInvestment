using ETFInvestment.Data.FinMind;
using ETFInvestment.Data.SQLite;
namespace ETFInvestment.Data.Services;

public sealed record UpdateOutcome(string Symbol,int PriceRows,int ReferenceEvents,bool RequiresManualReview,string Message);
public sealed class MarketDataUpdater
{
    private readonly FinMindClient _client;
    private readonly MarketRepository _repo;
    public MarketDataUpdater(FinMindClient client,MarketRepository repo) { _client=client;_repo=repo; }
    public async Task<UpdateOutcome> UpdateAsync(string symbol,DateOnly initialStart,DateOnly through,CancellationToken ct=default,bool backfill=false)
    {
        await _repo.InitializeAsync(ct);
        // Three-session overlap handles delayed daily updates and corrections. Upsert is idempotent.
        DateOnly? latest=await _repo.LatestDateAsync(symbol,ct);
        DateOnly from=!backfill && latest.HasValue && latest.Value.AddDays(-7)>initialStart ? latest.Value.AddDays(-7) : initialStart;
        if(from>through) return new UpdateOutcome(symbol,0,0,false,"Already up to date");
        // Only price rows are committed if we can validate BOTH datasets.
        // Never silently mark ETF split dates as ordinary 50% price collapses.
        var prices=await _client.GetPricesAsync(symbol,from,through,ct);
        IReadOnlyList<ETFInvestment.Core.Models.AdjustmentEvent> events;
        CorporateActionFetch splits;
        try { events=await _client.GetReferenceEventsAsync(symbol,from,through,ct); splits=await _client.GetSplitEventsAsync(symbol,from,through,ct); }
        catch(FinMindException ex)
        { return new UpdateOutcome(symbol,0,0,true,"Adjustment dataset unavailable or needs verification: "+ex.Message); }
        // A >35% one-session gap can be a split, but also a crash. Require independent confirmation.
        var prior=await _repo.GetPricesAsync(symbol,from.AddDays(-15),from.AddDays(-1),ct);
        var confirmedSplitDates=splits.Events.Select(e=>e.ExDate).ToHashSet();
        var combined=prior.Concat(prices).GroupBy(x=>x.Date).Select(g=>g.Last()).OrderBy(x=>x.Date).ToArray();
        for(int i=1;i<combined.Length;i++)
            if(Math.Abs(combined[i].Close/combined[i-1].Close-1m)>=0.35m && !confirmedSplitDates.Contains(combined[i].Date))
                return new UpdateOutcome(symbol,0,0,true,$"Large move on {combined[i].Date}: verify split/action before using signals");
        await _repo.UpsertPricesAsync(prices,ct);
        var allEvents=events.Concat(splits.Events).ToArray();
        if (allEvents.GroupBy(e=>e.ExDate).Any(g=>g.Count()>1))
            return new UpdateOutcome(symbol,0,0,true,"Overlapping dividend/split action requires independent review");
        await _repo.UpsertAdjustmentsAsync(symbol,allEvents,ct);
        return new UpdateOutcome(symbol,prices.Count,allEvents.Length,false,$"Data cached; dividend events={events.Count}, split events={splits.RawCount}. Verify action completeness independently.");
    }
}
