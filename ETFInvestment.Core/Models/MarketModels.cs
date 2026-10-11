namespace ETFInvestment.Core.Models;

/// <summary>Daily market close. Dates must be strictly increasing and unique in an input series.</summary>
public sealed record EtfPrice(string Symbol, DateOnly Date, decimal Close);

public enum AdjustmentKind { CashDividend, Split, ManualFactor }

/// <summary>
/// Multiplicative backwards adjustment: every historical date BEFORE ExDate is multiplied by Factor.
/// Factor for a dividend: (previous close - cash dividend) / previous close,
/// or use ReferencePrice / PreEventClose if the source supplies an authoritative reference price.
/// Factor for an N-for-1 split: 1/N. The event-date close itself is NOT adjusted by this event.
/// Factors are price-continuity tools and NOT a substitute for full cash-flow total return.
/// </summary>
public sealed record AdjustmentEvent(DateOnly ExDate, decimal Factor, AdjustmentKind Kind, string Source);

public sealed record TradeRecord(string Symbol, DateOnly Date, int Shares, decimal UnitPrice, decimal Fees, bool IsBuy);
public sealed record CashDividend(string Symbol, DateOnly ExDate, DateOnly PayDate, decimal AmountPerShare);

public enum SignalStatus { Watch, CandidateBuy, DueBuy, BudgetExhausted, AwaitingData }
public sealed record BuySignal(SignalStatus Status, string Reason, int SuggestedShares, decimal? ReferencePrice = null);

public sealed record CoreEtfRules(
    decimal InitialDrawdown,
    decimal NextDrawdown = 0.05m,
    int HighLookback = 20,
    int TradingDayInterval = 20,
    int MaxSharesPerOrder = 2000,
    int TargetInitialShares = 2000,
    decimal DueInitialFraction = 0.5m,
    decimal FeeRate = 0.001425m,
    decimal FeeMultiplier = 1m,
    decimal MinimumFee = 0m);

public sealed record CoreEtfState(
    string Symbol,
    decimal Budget,
    DateOnly StartDate,
    DateOnly FinalDeadline,
    decimal Spent,
    IReadOnlyList<TradeRecord> Trades);

public sealed record RebalanceState(decimal Units, decimal Cash, decimal BasePrice);
public enum RebalanceAction { None, Buy, Sell, AwaitingData }
public sealed record RebalanceSignal(RebalanceAction Action, decimal TargetAmount, decimal TargetUnits, decimal NewStockValue, decimal NewCash, string Reason);
