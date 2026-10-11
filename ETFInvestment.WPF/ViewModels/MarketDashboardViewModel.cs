using ETFInvestment.Core;
using ETFInvestment.Core.Models;
using ETFInvestment.Data.SQLite;
using System.IO;

namespace ETFInvestment.WPF.ViewModels;

public sealed record EtfDefinition(string Symbol, string Name);
public sealed record MarketSnapshot(EtfDefinition Etf, IReadOnlyList<EtfPrice> Raw,
    IReadOnlyList<EtfPrice> Adjusted, AdjustedPriceAudit.Report Audit)
{
    public EtfPrice? Latest => Raw.Count == 0 ? null : Raw[^1];
    public decimal? TwentyDayReturn => Adjusted.Count >= 21
        ? Adjusted[^1].Close / Adjusted[^21].Close - 1m : null;
    public string AuditLabel => Audit.HasBlockingFindings ? "待確認：價格異常" : "內部檢查通過／來源未驗證";
}

/// <summary>Read-only market dashboard. No API tokens, no portfolio database, no trading signals.</summary>
public sealed class MarketDashboardViewModel
{
    private readonly MarketRepository _repository = new();
    public static readonly EtfDefinition[] Etfs =
    [
        new("0050", "元大台灣50"), new("0056", "元大高股息"),
        new("00878", "國泰永續高股息"), new("00631L", "元大台灣50正2")
    ];

    public async Task<MarketSnapshot> LoadAsync(EtfDefinition etf, CancellationToken ct = default)
    {
        // Do NOT create an empty market.db to pretend data were downloaded.
        if (!File.Exists(DatabasePaths.Market))
            throw new FileNotFoundException("找不到行情快取。請先使用 Collector 回補四檔 ETF 歷史行情。", DatabasePaths.Market);
        var raw = await _repository.GetPricesAsync(etf.Symbol, new DateOnly(2000, 1, 1),
            DateOnly.FromDateTime(DateTime.Today), ct);
        var events = await _repository.GetAdjustmentsAsync(etf.Symbol, ct);
        var report = AdjustedPriceAudit.Check(etf.Symbol, raw, events);
        return new MarketSnapshot(etf, raw, report.AdjustedPrices, report);
    }
}
