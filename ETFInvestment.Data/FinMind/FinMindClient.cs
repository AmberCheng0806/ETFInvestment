using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ETFInvestment.Core.Models;

namespace ETFInvestment.Data.FinMind;

public sealed class FinMindException : Exception
{
    public int? HttpStatus { get; }
    public FinMindException(string message, int? httpStatus = null) : base(message) => HttpStatus = httpStatus;
}

/// <summary>HTTP and FinMind JSON parsing only. Never puts the token in query parameters or logs.</summary>
public sealed class FinMindClient
{
    private readonly HttpClient _http;
    private readonly string? _token;
    private readonly string _endpoint;
    public FinMindClient(HttpClient http, string? token, string endpoint = "https://api.finmindtrade.com/api/v4/data")
    {
        _http = http;
        _token = token;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("FinMind endpoint must use HTTPS", nameof(endpoint));
        _endpoint = endpoint;
    }

    public async Task<IReadOnlyList<EtfPrice>> GetPricesAsync(string symbol, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        using var json = await FetchAsync("TaiwanStockPrice", symbol, start, end, ct);
        var list = new List<EtfPrice>();
        foreach (var row in json.RootElement.GetProperty("data").EnumerateArray())
        {
            var id = GetText(row,"stock_id");
            if (id != symbol) continue;
            if (!DateOnly.TryParseExact(GetText(row,"date"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)) throw new FinMindException("Invalid price date");
            if (!TryDecimal(row,"close",out var close) || close <= 0) throw new FinMindException("Invalid close for "+symbol+" "+date);
            if (date >= start && date <= end) list.Add(new EtfPrice(symbol,date,close));
        }
        var ordered = list.OrderBy(p=>p.Date).ToArray();
        if (ordered.Select(p=>p.Date).Distinct().Count()!=ordered.Length) throw new FinMindException("Duplicate price date; refusing update");
        return ordered;
    }

    /// <summary>DividendResult provides pre-event price/reference price, not a guaranteed stock-split history.</summary>
    public async Task<IReadOnlyList<AdjustmentEvent>> GetReferenceEventsAsync(string symbol, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        using var json = await FetchAsync("TaiwanStockDividendResult",symbol,start,end,ct);
        var list = new List<AdjustmentEvent>();
        foreach(var row in json.RootElement.GetProperty("data").EnumerateArray())
        {
            if (GetText(row,"stock_id") != symbol) continue;
            if (!DateOnly.TryParseExact(GetText(row,"date"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)) throw new FinMindException("Invalid adjustment date");
            if (!TryDecimal(row,"before_price",out var before) || !TryDecimal(row,"reference_price",out var reference) || before<=0 || reference<=0)
                throw new FinMindException("Missing reliable reference prices; manual verification needed");
            if (before == reference) continue;
            var eventType = GetText(row,"stock_or_cache_dividend");
            // Stock entitlement may not equal ETF unit splits. Flag everything non-cash for manual review.
            if (eventType.Contains('權') || (!eventType.Contains('息')))
                throw new FinMindException($"Non-cash corporate action on {day}: manual verification required before signals");
            list.Add(ETFInvestment.Core.AdjustedSeries.FromReferencePrice(day,before,reference,AdjustmentKind.CashDividend,"FinMind:TaiwanStockDividendResult"));
        }
        return list.OrderBy(x=>x.ExDate).ToArray();
    }

    private async Task<JsonDocument> FetchAsync(string dataset,string symbol,DateOnly start,DateOnly end,CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol) || end < start) throw new ArgumentException("Invalid symbol/date range");
        string url = _endpoint + "?dataset=" + Uri.EscapeDataString(dataset) + "&data_id=" + Uri.EscapeDataString(symbol)
            + "&start_date=" + start.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)
            + "&end_date=" + end.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        const int maxAttempts=3;
        for(int attempt=1;attempt<=maxAttempts;attempt++)
        {
            using var req=new HttpRequestMessage(HttpMethod.Get,url);
            if(!string.IsNullOrWhiteSpace(_token)) req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_token);
            using var response=await _http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct);
            int status=(int)response.StatusCode;
            if(status == 429 || status >= 500)
            {
                if(attempt<maxAttempts) { await Task.Delay(TimeSpan.FromMilliseconds(150*attempt),ct); continue; }
                throw new FinMindException("Transient FinMind failure; retry later",status);
            }
            if(!response.IsSuccessStatusCode) throw new FinMindException("FinMind HTTP failure. Check dataset permissions/credentials.",status);
            await using var stream=await response.Content.ReadAsStreamAsync(ct);
            JsonDocument json;
            try { json=await JsonDocument.ParseAsync(stream,cancellationToken:ct); }
            catch(JsonException e) { throw new FinMindException("FinMind response was invalid JSON: "+e.Message); }
            if(!json.RootElement.TryGetProperty("data",out var data) || data.ValueKind!=JsonValueKind.Array)
            { json.Dispose(); throw new FinMindException("FinMind response missing data array or returned an API error"); }
            if (json.RootElement.TryGetProperty("status",out var apiStatus) && apiStatus.ValueKind==JsonValueKind.Number &&
                apiStatus.TryGetInt32(out int code) && code != 200)
            { json.Dispose(); throw new FinMindException("FinMind rejected the dataset request",code); }
            return json;
        }
        throw new FinMindException("Unexpected retry exhaustion");
    }

    private static string GetText(JsonElement row,string name)
    {
        if(!row.TryGetProperty(name,out var v) || v.ValueKind==JsonValueKind.Null) return "";
        return v.ValueKind==JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
    }
    private static bool TryDecimal(JsonElement row,string name,out decimal value) =>
        decimal.TryParse(GetText(row,name),NumberStyles.Float,CultureInfo.InvariantCulture,out value);
}
