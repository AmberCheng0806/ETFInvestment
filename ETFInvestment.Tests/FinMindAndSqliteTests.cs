using System.Net;
using System.Net.Http;
using System.Text;
using ETFInvestment.Core.Models;
using ETFInvestment.Data.FinMind;
using ETFInvestment.Data.SQLite;
using ETFInvestment.Data.Services;
using Xunit;
namespace ETFInvestment.Tests;

public sealed class FinMindAndSqliteTests
{
    private sealed class MockHandler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) => Task.FromResult(respond(request));
    }
    private static FinMindClient Client(Func<HttpRequestMessage,HttpResponseMessage> respond) => new(new HttpClient(new MockHandler(respond)),"TEST_TOKEN");
    private static HttpResponseMessage Json(string json,HttpStatusCode status=HttpStatusCode.OK) => new(status){Content=new StringContent(json,Encoding.UTF8,"application/json")};
    private static string Tmp() {var p=Path.Combine(Path.GetTempPath(),"ETFTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}

    [Fact] public async Task ParsesFinMindPriceAndUsesBearerHeader()
    {
        var client=Client(req=>
        {
            Assert.Contains("dataset=TaiwanStockPrice",req.RequestUri!.Query);
            Assert.Equal("Bearer",req.Headers.Authorization!.Scheme);
            Assert.Equal("TEST_TOKEN",req.Headers.Authorization.Parameter);
            return Json("""{"status":200,"data":[{"date":"2026-01-02","stock_id":"0050","close":50.75},{"date":"2026-01-01","stock_id":"0050","close":50.25}]}""");
        });
        var rows=await client.GetPricesAsync("0050",new DateOnly(2026,1,1),new DateOnly(2026,1,3));
        Assert.Equal(2,rows.Count);Assert.Equal(new DateOnly(2026,1,1),rows[0].Date);Assert.Equal(50.75m,rows[1].Close);
    }
    [Fact] public async Task ParsesReferencePriceAndRejectsUnconfirmedSplit()
    {
        var client=Client(_=>Json("""{"status":200,"data":[{"date":"2026-05-03","stock_id":"0056","before_price":40,"reference_price":39,"stock_or_cache_dividend":"除息"}]}"""));
        var events=await client.GetReferenceEventsAsync("0056",new DateOnly(2026,1,1),new DateOnly(2026,12,31));
        Assert.Single(events);Assert.Equal(0.975m,events[0].Factor);
        var split=Client(_=>Json("""{"status":200,"data":[{"date":"2026-03-01","stock_id":"00631L","before_price":200,"reference_price":20,"stock_or_cache_dividend":"除權"}]}"""));
        await Assert.ThrowsAsync<FinMindException>(()=>split.GetReferenceEventsAsync("00631L",new DateOnly(2026,1,1),new DateOnly(2026,12,31)));
    }
    [Fact] public async Task UnauthorizedIsNotRetried()
    {
        int count=0;var client=Client(_=>{count++;return Json("{}",HttpStatusCode.Unauthorized);});
        var ex=await Assert.ThrowsAsync<FinMindException>(()=>client.GetPricesAsync("0050",new DateOnly(2026,1,1),new DateOnly(2026,1,2)));
        Assert.Equal(401,ex.HttpStatus);Assert.Equal(1,count);
    }
    [Fact] public async Task MarketDatabaseUpsertsAndSorts()
    {
        string folder=Tmp();try
        {
            var repo=new MarketRepository(Path.Combine(folder,"market.db"));await repo.InitializeAsync();
            var date=new DateOnly(2026,1,2);await repo.UpsertPricesAsync([new("0050",date,50m)]);
            await repo.UpsertPricesAsync([new("0050",date,51m)]);
            var rows=await repo.GetPricesAsync("0050",date,date);Assert.Single(rows);Assert.Equal(51m,rows[0].Close);
            Assert.Equal(date,await repo.LatestDateAsync("0050"));
        }finally{Directory.Delete(folder,true);}
    }
    [Fact] public async Task PortfolioPersistsTransactionsDividendAndBackup()
    {
        string folder=Tmp();try
        {
            var path=Path.Combine(folder,"portfolio.db");var repo=new PortfolioRepository(path);await repo.InitializeAsync();
            await repo.SaveCorePlanAsync("0056",400000m,new DateOnly(2026,1,1),new DateOnly(2027,1,1));
            await repo.SaveTradeAsync(new TradeRecord("0056",new DateOnly(2026,1,5),2000,35m,50m,true));
            await repo.SaveDividendAsync(new CashDividend("0056",new DateOnly(2026,3,1),new DateOnly(2026,3,25),0.7m));
            await repo.SaveRebalanceStateAsync("00631L",new RebalanceState(1000m,50000m,50m));
            var reopen=new PortfolioRepository(path);var state=await reopen.GetCoreStateAsync("0056");
            Assert.NotNull(state);Assert.Single(state.Trades);Assert.Equal(70050m,state.Spent);
            Assert.Single(await reopen.GetDividendsAsync("0056"));Assert.Equal(50m,(await reopen.GetRebalanceStateAsync("00631L"))!.BasePrice);
            var backup=await SqliteDatabase.BackupPortfolioAsync(path,Path.Combine(folder,"backup"));
            Assert.True(File.Exists(backup));var restored=new PortfolioRepository(backup);
            Assert.Single(await restored.GetTradesAsync("0056"));
        }finally{Directory.Delete(folder,true);}
    }
    [Fact] public async Task IncrementalUpdateBlocksAbnormalPriceMove()
    {
        string folder=Tmp();try
        {
            var repo=new MarketRepository(Path.Combine(folder,"market.db"));
            var client=Client(req=>req.RequestUri!.Query.Contains("TaiwanStockPrice")
                ? Json("""{"status":200,"data":[{"date":"2026-03-01","stock_id":"00631L","close":200},{"date":"2026-03-02","stock_id":"00631L","close":20}]}""")
                : Json("""{"status":200,"data":[]}"""));
            var result=await new MarketDataUpdater(client,repo).UpdateAsync("00631L",new DateOnly(2026,3,1),new DateOnly(2026,3,2));
            Assert.True(result.RequiresManualReview);
            Assert.Empty(await repo.GetPricesAsync("00631L",new DateOnly(2026,3,1),new DateOnly(2026,3,2)));
        }finally{Directory.Delete(folder,true);}
    }
}
