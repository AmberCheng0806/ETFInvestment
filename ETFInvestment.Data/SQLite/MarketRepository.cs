using System.Globalization;
using ETFInvestment.Core.Interfaces;
using ETFInvestment.Core.Models;
using Microsoft.Data.Sqlite;
namespace ETFInvestment.Data.SQLite;
public sealed class MarketRepository : IMarketDataRepository
{
    private readonly string _path;
    public MarketRepository(string? path=null) => _path=path ?? DatabasePaths.Market;
    public Task InitializeAsync(CancellationToken ct=default)=>SqliteDatabase.InitializeMarketAsync(_path,ct);
    public async Task UpsertPricesAsync(IEnumerable<EtfPrice> prices,CancellationToken ct=default)
    {
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);
        using var tx=db.BeginTransaction();
        foreach(var p in prices)
        {
            if(p.Close<=0 || string.IsNullOrWhiteSpace(p.Symbol)) throw new ArgumentException("Invalid market price");
            await using var cmd=db.CreateCommand(); cmd.Transaction=tx;
            cmd.CommandText="INSERT INTO Prices(Symbol,TradeDate,CloseText) VALUES($s,$d,$p) ON CONFLICT(Symbol,TradeDate) DO UPDATE SET CloseText=excluded.CloseText";
            cmd.Parameters.AddWithValue("$s",p.Symbol);cmd.Parameters.AddWithValue("$d",p.Date.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$p",p.Close.ToString(CultureInfo.InvariantCulture));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        tx.Commit();
    }
    public async Task UpsertAdjustmentsAsync(string symbol,IEnumerable<AdjustmentEvent> events,CancellationToken ct=default)
    {
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);
        using var tx=db.BeginTransaction();
        foreach(var e in events)
        {
            if(e.Factor<=0 || string.IsNullOrWhiteSpace(e.Source)) throw new ArgumentException("Invalid adjustment");
            await using var cmd=db.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="INSERT INTO Adjustments(Symbol,ExDate,FactorText,Kind,Source) VALUES($s,$d,$f,$k,$source) ON CONFLICT(Symbol,ExDate) DO UPDATE SET FactorText=excluded.FactorText,Kind=excluded.Kind,Source=excluded.Source";
            cmd.Parameters.AddWithValue("$s",symbol);cmd.Parameters.AddWithValue("$d",e.ExDate.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$f",e.Factor.ToString(CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$k",(int)e.Kind);cmd.Parameters.AddWithValue("$source",e.Source);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        tx.Commit();
    }
    public async Task<IReadOnlyList<EtfPrice>> GetPricesAsync(string symbol,DateOnly from,DateOnly to,CancellationToken ct=default)
    {
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);
        await using var cmd=db.CreateCommand();cmd.CommandText="SELECT TradeDate,CloseText FROM Prices WHERE Symbol=$s AND TradeDate BETWEEN $a AND $b ORDER BY TradeDate";
        cmd.Parameters.AddWithValue("$s",symbol);cmd.Parameters.AddWithValue("$a",from.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$b",to.ToString("yyyy-MM-dd"));
        var result=new List<EtfPrice>();await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) result.Add(new EtfPrice(symbol,DateOnly.ParseExact(r.GetString(0),"yyyy-MM-dd",CultureInfo.InvariantCulture),decimal.Parse(r.GetString(1),CultureInfo.InvariantCulture)));
        return result;
    }
    public async Task<IReadOnlyList<AdjustmentEvent>> GetAdjustmentsAsync(string symbol,CancellationToken ct=default)
    {
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);
        await using var cmd=db.CreateCommand();cmd.CommandText="SELECT ExDate,FactorText,Kind,Source FROM Adjustments WHERE Symbol=$s ORDER BY ExDate";cmd.Parameters.AddWithValue("$s",symbol);
        var list=new List<AdjustmentEvent>();await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)) list.Add(new AdjustmentEvent(DateOnly.ParseExact(r.GetString(0),"yyyy-MM-dd",CultureInfo.InvariantCulture),decimal.Parse(r.GetString(1),CultureInfo.InvariantCulture),(AdjustmentKind)r.GetInt32(2),r.GetString(3)));
        return list;
    }
    public async Task<DateOnly?> LatestDateAsync(string symbol,CancellationToken ct=default)
    {
        await using var db=await SqliteDatabase.OpenAsync(_path,ct);
        await using var cmd=db.CreateCommand();cmd.CommandText="SELECT MAX(TradeDate) FROM Prices WHERE Symbol=$s";cmd.Parameters.AddWithValue("$s",symbol);
        var v=await cmd.ExecuteScalarAsync(ct);
        return v is string s ? DateOnly.ParseExact(s,"yyyy-MM-dd",CultureInfo.InvariantCulture) : null;
    }
}
