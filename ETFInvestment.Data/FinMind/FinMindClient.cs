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
public sealed record CorporateActionFetch(IReadOnlyList<AdjustmentEvent> Events, int RawCount);

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

    /// <summary>FinMind TaiwanStockSplitPrice is a market-wide dataset. Do not send data_id; filter locally.</summary>
    public async Task<CorporateActionFetch> GetSplitEventsAsync(string symbol, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        using var json = await FetchAsync("TaiwanStockSplitPrice", null, start, end, ct);
        var events = new List<AdjustmentEvent>();
        int matched = 0;
        foreach (var row in json.RootElement.GetProperty("data").EnumerateArray())
        {
            if (GetText(row,"stock_id") != symbol) continue;
            if (!DateOnly.TryParseExact(GetText(row,"date"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))
                throw new FinMindException("Invalid split date");
            if (day < start || day > end) continue;
            matched++;
            string type = GetText(row,"type");
            if (!TryDecimal(row,"before_price",out var before) || !TryDecimal(row,"after_price",out var after) || before <= 0 || after <= 0)
                throw new FinMindException($"Missing split prices on {day}");

            // Some real ETF split records may use a type label other than the plain Chinese word "分割".
            // Do not generalize unknown types: accept only the precise 00631L event independently
            // corroborated by the Taiwan Stock Exchange's 2026-03-30 issuer announcement.
            // Exchange announcement: 22 new units per 1 old unit, previous close 443.15,
            // reference reopening price 20.14 on 2026-03-31 (rounded to cents).
            bool verified00631LSplit = symbol == "00631L" && day == new DateOnly(2026,3,31)
                && Math.Abs(before - 443.15m) <= 0.02m
                && Math.Abs(after - 20.14m) <= 0.02m
                && Math.Abs(before / after - 22m) <= 0.1m;

            if (!type.Contains("分割",StringComparison.Ordinal) && !verified00631LSplit)
                throw new FinMindException($"Unknown split type on {day}: type='{type}', before={before}, after={after}; manual review required");

            // For the confirmed 00631L event, use the exact official 1/22 split ratio,
            // not 20.14 / 443.15 which is affected by reference-price rounding.
            events.Add(verified00631LSplit
                ? new AdjustmentEvent(day, 1m / 22m, AdjustmentKind.Split,
                    "TWSE:00631L:2026-03-30:22-to-1 + FinMind:TaiwanStockSplitPrice")
                : ETFInvestment.Core.AdjustedSeries.FromReferencePrice(day,before,after,
                    AdjustmentKind.Split,"FinMind:TaiwanStockSplitPrice"));
        }
        return new CorporateActionFetch(events.OrderBy(x=>x.ExDate).ToArray(),matched);
    }

    private async Task<JsonDocument> FetchAsync(string dataset,string? symbol,DateOnly start,DateOnly end,CancellationToken ct)
    {
        if (end < start) throw new ArgumentException("Invalid date range");
        string url = _endpoint + "?dataset=" + Uri.EscapeDataString(dataset) + (symbol is null ? "" : "&data_id=" + Uri.EscapeDataString(symbol))
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
