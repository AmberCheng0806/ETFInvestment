using System.Globalization;
using ETFInvestment.Core.Interfaces;
using ETFInvestment.Core.Models;
namespace ETFInvestment.Data.SQLite;
public sealed class PortfolioRepository : IPortfolioRepository
{
    private readonly string _path;
    public PortfolioRepository(string? path=null)=>_path=path??DatabasePaths.Portfolio;
    public Task InitializeAsync(CancellationToken ct=default)=>SqliteDatabase.InitializePortfolioAsync(_path,ct);
    public async Task SaveCorePlanAsync(string symbol,decimal budget,DateOnly start,DateOnly deadline,CancellationToken ct=default)
    {
        if(budget<=0||deadline<start) throw new ArgumentException("Invalid private plan");
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);
        await using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO CoreState(Symbol,BudgetText,StartDate,FinalDeadline) VALUES($s,$b,$d,$e) ON CONFLICT(Symbol) DO UPDATE SET BudgetText=excluded.BudgetText,StartDate=excluded.StartDate,FinalDeadline=excluded.FinalDeadline";
        cmd.Parameters.AddWithValue("$s",symbol);cmd.Parameters.AddWithValue("$b",budget.ToString(CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$d",start.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$e",deadline.ToString("yyyy-MM-dd"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<CoreEtfState?> GetCoreStateAsync(string symbol,CancellationToken ct=default)
    {
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);
        await using var cmd=db.CreateCommand();cmd.CommandText="SELECT BudgetText,StartDate,FinalDeadline FROM CoreState WHERE Symbol=$s";cmd.Parameters.AddWithValue("$s",symbol);
        decimal budget;DateOnly start,deadline;
        await using(var r=await cmd.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct)) return null;
            budget=decimal.Parse(r.GetString(0),CultureInfo.InvariantCulture);start=DateOnly.ParseExact(r.GetString(1),"yyyy-MM-dd",CultureInfo.InvariantCulture);deadline=DateOnly.ParseExact(r.GetString(2),"yyyy-MM-dd",CultureInfo.InvariantCulture);
        }
        var trades=await GetTradesAsync(symbol,ct);
        // Spent is cumulative purchase cost, minus executed sell proceeds (net of fees).
        decimal spent=trades.Sum(t=>t.IsBuy ? t.UnitPrice*t.Shares+t.Fees : -(t.UnitPrice*t.Shares-t.Fees));
        return new CoreEtfState(symbol,budget,start,deadline,spent,trades);
    }
    public async Task<IReadOnlyList<TradeRecord>> GetTradesAsync(string symbol,CancellationToken ct=default)
    {
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);
        await using var cmd=db.CreateCommand();cmd.CommandText="SELECT TradeDate,Shares,UnitPriceText,FeesText,IsBuy FROM Trades WHERE Symbol=$s ORDER BY TradeDate,Id";cmd.Parameters.AddWithValue("$s",symbol);
        var list=new List<TradeRecord>();await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) list.Add(new TradeRecord(symbol,DateOnly.ParseExact(r.GetString(0),"yyyy-MM-dd",CultureInfo.InvariantCulture),r.GetInt32(1),decimal.Parse(r.GetString(2),CultureInfo.InvariantCulture),decimal.Parse(r.GetString(3),CultureInfo.InvariantCulture),r.GetInt32(4)==1));
        return list;
    }
    public async Task SaveTradeAsync(TradeRecord trade,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(trade.Symbol)||trade.Shares<=0||trade.UnitPrice<=0||trade.Fees<0) throw new ArgumentException("Invalid trade");
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);await using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO Trades(Symbol,TradeDate,Shares,UnitPriceText,FeesText,IsBuy) VALUES($s,$d,$q,$p,$f,$b)";
        cmd.Parameters.AddWithValue("$s",trade.Symbol);cmd.Parameters.AddWithValue("$d",trade.Date.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$q",trade.Shares);cmd.Parameters.AddWithValue("$p",trade.UnitPrice.ToString(CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$f",trade.Fees.ToString(CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$b",trade.IsBuy?1:0);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task SaveDividendAsync(CashDividend dividend,CancellationToken ct=default)
    {
        if(dividend.AmountPerShare<0 || string.IsNullOrWhiteSpace(dividend.Symbol)) throw new ArgumentException("Invalid dividend");
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);await using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO Dividends(Symbol,ExDate,PayDate,AmountText) VALUES($s,$d,$p,$a) ON CONFLICT(Symbol,ExDate,PayDate) DO UPDATE SET AmountText=excluded.AmountText";
        cmd.Parameters.AddWithValue("$s",dividend.Symbol);cmd.Parameters.AddWithValue("$d",dividend.ExDate.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$p",dividend.PayDate.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$a",dividend.AmountPerShare.ToString(CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<IReadOnlyList<CashDividend>> GetDividendsAsync(string symbol,CancellationToken ct=default)
    {
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);await using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT ExDate,PayDate,AmountText FROM Dividends WHERE Symbol=$s ORDER BY ExDate";cmd.Parameters.AddWithValue("$s",symbol);
        var list=new List<CashDividend>();await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) list.Add(new CashDividend(symbol,DateOnly.ParseExact(r.GetString(0),"yyyy-MM-dd",CultureInfo.InvariantCulture),DateOnly.ParseExact(r.GetString(1),"yyyy-MM-dd",CultureInfo.InvariantCulture),decimal.Parse(r.GetString(2),CultureInfo.InvariantCulture)));
        return list;
    }
    public async Task SaveRebalanceStateAsync(string symbol,RebalanceState state,CancellationToken ct=default)
    {
        if(state.Units<0||state.Cash<0||state.BasePrice<=0) throw new ArgumentException("Invalid 50:50 state");
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);await using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO RebalanceState(Symbol,UnitsText,CashText,BasePriceText,UpdatedUtc) VALUES($s,$u,$c,$b,$t) ON CONFLICT(Symbol) DO UPDATE SET UnitsText=excluded.UnitsText,CashText=excluded.CashText,BasePriceText=excluded.BasePriceText,UpdatedUtc=excluded.UpdatedUtc";
        cmd.Parameters.AddWithValue("$s",symbol);cmd.Parameters.AddWithValue("$u",state.Units.ToString(CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$c",state.Cash.ToString(CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$b",state.BasePrice.ToString(CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$t",DateTime.UtcNow.ToString("O"));await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<RebalanceState?> GetRebalanceStateAsync(string symbol,CancellationToken ct=default)
    {
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);await using var cmd=db.CreateCommand();cmd.CommandText="SELECT UnitsText,CashText,BasePriceText FROM RebalanceState WHERE Symbol=$s";cmd.Parameters.AddWithValue("$s",symbol);
        await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct)) return null;
        return new RebalanceState(decimal.Parse(r.GetString(0),CultureInfo.InvariantCulture),decimal.Parse(r.GetString(1),CultureInfo.InvariantCulture),decimal.Parse(r.GetString(2),CultureInfo.InvariantCulture));
    }
}
