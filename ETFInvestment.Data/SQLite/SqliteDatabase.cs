using Microsoft.Data.Sqlite;
namespace ETFInvestment.Data.SQLite;
public static class SqliteDatabase
{
    public static async Task InitializeMarketAsync(string path,CancellationToken ct=default)
    {
        await using var db=await OpenAsync(path,ct);
        await RunAsync(db,@"CREATE TABLE IF NOT EXISTS Prices(Symbol TEXT NOT NULL,TradeDate TEXT NOT NULL,CloseText TEXT NOT NULL,PRIMARY KEY(Symbol,TradeDate));
CREATE TABLE IF NOT EXISTS Adjustments(Symbol TEXT NOT NULL,ExDate TEXT NOT NULL,FactorText TEXT NOT NULL,Kind INTEGER NOT NULL,Source TEXT NOT NULL,PRIMARY KEY(Symbol,ExDate));
CREATE TABLE IF NOT EXISTS UpdateState(Symbol TEXT NOT NULL,Dataset TEXT NOT NULL,LastCheckedUtc TEXT NOT NULL,PRIMARY KEY(Symbol,Dataset));",ct);
    }
    public static async Task InitializePortfolioAsync(string path,CancellationToken ct=default)
    {
        await using var db=await OpenAsync(path,ct);
        await RunAsync(db,@"CREATE TABLE IF NOT EXISTS CoreState(Symbol TEXT PRIMARY KEY,BudgetText TEXT NOT NULL,StartDate TEXT NOT NULL,FinalDeadline TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Trades(Id INTEGER PRIMARY KEY AUTOINCREMENT,Symbol TEXT NOT NULL,TradeDate TEXT NOT NULL,Shares INTEGER NOT NULL,UnitPriceText TEXT NOT NULL,FeesText TEXT NOT NULL,IsBuy INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS Dividends(Symbol TEXT NOT NULL,ExDate TEXT NOT NULL,PayDate TEXT NOT NULL,AmountText TEXT NOT NULL,PRIMARY KEY(Symbol,ExDate,PayDate));
CREATE TABLE IF NOT EXISTS RebalanceState(Symbol TEXT PRIMARY KEY,UnitsText TEXT NOT NULL,CashText TEXT NOT NULL,BasePriceText TEXT NOT NULL,UpdatedUtc TEXT NOT NULL);",ct);
    }
    public static async Task<SqliteConnection> OpenAsync(string path,CancellationToken ct=default)
    {
        string? dir=Path.GetDirectoryName(Path.GetFullPath(path));
        if(dir!=null) Directory.CreateDirectory(dir);
        var c=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=path,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
        await c.OpenAsync(ct);
        await RunAsync(c,"PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;",ct);
        return c;
    }
    internal static async Task RunAsync(SqliteConnection db,string sql,CancellationToken ct=default)
    {
        await using var cmd=db.CreateCommand(); cmd.CommandText=sql; await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>SQLite backup API: consistent snapshot even if the source is using WAL.</summary>
    public static async Task<string> BackupPortfolioAsync(string source,string backupDirectory,CancellationToken ct=default)
    {
        if(!File.Exists(source)) throw new FileNotFoundException("Private portfolio database not found",source);
        Directory.CreateDirectory(backupDirectory);
        var destination=Path.Combine(backupDirectory,"portfolio-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".db");
        await using var from=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=source,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        await from.OpenAsync(ct);
        await using var to=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=destination,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
        await to.OpenAsync(ct);
        from.BackupDatabase(to);
        return destination;
    }
}
