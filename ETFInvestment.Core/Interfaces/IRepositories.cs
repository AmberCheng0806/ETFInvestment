using ETFInvestment.Core.Models;
namespace ETFInvestment.Core.Interfaces;

public interface IMarketDataRepository
{
    Task<IReadOnlyList<EtfPrice>> GetPricesAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdjustmentEvent>> GetAdjustmentsAsync(string symbol, CancellationToken cancellationToken = default);
}

public interface IPortfolioRepository
{
    Task<CoreEtfState?> GetCoreStateAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradeRecord>> GetTradesAsync(string symbol, CancellationToken cancellationToken = default);
    Task SaveTradeAsync(TradeRecord trade, CancellationToken cancellationToken = default);
    Task SaveDividendAsync(CashDividend dividend, CancellationToken cancellationToken = default);
}
