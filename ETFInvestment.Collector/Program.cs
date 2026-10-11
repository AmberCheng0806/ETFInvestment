using System.Globalization;
using ETFInvestment.Core.Models;
using ETFInvestment.Data.FinMind;
using ETFInvestment.Data.Services;
using ETFInvestment.Data.SQLite;
using Microsoft.Extensions.Configuration;

// Market-only collector. NEVER loads or writes portfolio.db.
// No live trading, no investment advice, and no private portfolio data.
internal sealed class SecretsMarker { }

internal static class Program
{
    private static readonly string[] DefaultSymbols = ["0050", "0056", "00878", "00631L"];

    public static async Task<int> Main(string[] args)
    {
        if (args.Any(x => x is "-h" or "--help"))
        {
            PrintUsage();
            return 0;
        }

        DateOnly start = new(2025, 1, 1);
        DateOnly end = DateOnly.FromDateTime(DateTime.Today);
        bool pricesOnly = false;
        bool backfill = false;
        string[] symbols = DefaultSymbols;

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--from": start = ParseDate(GetValue(args, ref i)); break;
                    case "--to": end = ParseDate(GetValue(args, ref i)); break;
                    case "--symbols":
                        symbols = GetValue(args, ref i).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        if (symbols.Length == 0 || symbols.Any(s => !DefaultSymbols.Contains(s)))
                            throw new ArgumentException("--symbols accepts only 0050,0056,00878,00631L");
                        break;
                    case "--prices-only": pricesOnly = true; break;
                    case "--backfill": backfill = true; break;
                    default: throw new ArgumentException($"Unknown argument: {args[i]}");
                }
            }
            if (start > end) throw new ArgumentException("--from cannot be later than --to");
            if (end > DateOnly.FromDateTime(DateTime.Today)) throw new ArgumentException("--to cannot be in the future");
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            Console.Error.WriteLine("Argument error: " + ex.Message);
            PrintUsage();
            return 2;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddUserSecrets<SecretsMarker>(optional: true)
            .Build();
        // Environment variables override user secrets. No token is ever printed.
        string? token = Environment.GetEnvironmentVariable("FinMind__Token")
            ?? Environment.GetEnvironmentVariable("FINMIND_TOKEN")
            ?? configuration["FinMind:Token"];

        Console.WriteLine("ETFInvestment Collector v2.3 — market data verification only");
        Console.WriteLine($"Date range: {start:yyyy-MM-dd} to {end:yyyy-MM-dd}; mode: {(pricesOnly ? "PRICE-ONLY" : "PRICE + DIVIDEND + SPLIT")} ; backfill={backfill}");
        Console.WriteLine("Token configured: " + (!string.IsNullOrWhiteSpace(token) ? "YES" : "NO (anonymous quota may apply)"));
        Console.WriteLine("Market database: " + DatabasePaths.Market);
        Console.WriteLine("Private portfolio.db: NOT ACCESSED");
        Console.WriteLine();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ETFInvestment-Collector/2.3");
        var client = new FinMindClient(http, token);
        var repository = new MarketRepository();
        try { await repository.InitializeAsync(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Cannot initialize market.db: " + ex.Message);
            return 1;
        }

        int errors = 0;
        foreach (string symbol in symbols.Distinct(StringComparer.Ordinal))
        {
            try
            {
                DateOnly? oldLatest = await repository.LatestDateAsync(symbol);
                if (pricesOnly)
                {
                    DateOnly from = !backfill && oldLatest is { } latest && latest.AddDays(-7) > start ? latest.AddDays(-7) : start;
                    IReadOnlyList<EtfPrice> records = from <= end
                        ? await client.GetPricesAsync(symbol, from, end)
                        : [];
                    if (records.Count == 0 && oldLatest is null)
                        throw new FinMindException("Empty price data: check ticker, permissions and requested date range");
                    // A price-only update is NOT verified for splits/dividends. Never use for live signals.
                    var previous = await repository.GetPricesAsync(symbol, from.AddDays(-15), from.AddDays(-1));
                    var all = previous.Concat(records).GroupBy(x => x.Date).Select(g => g.Last()).OrderBy(x => x.Date).ToArray();
                    for (int n = 1; n < all.Length; n++)
                        if (Math.Abs(all[n].Close / all[n - 1].Close - 1m) >= .35m)
                            throw new FinMindException($"Large move on {all[n].Date}; halted to require corporate-action verification");
                    await repository.UpsertPricesAsync(records);
                    var newLatest = await repository.LatestDateAsync(symbol);
                    Console.WriteLine($"[PRICE ONLY / UNVERIFIED ACTIONS] {symbol}: API rows={records.Count}, cached latest={newLatest:yyyy-MM-dd}");
                }
                else
                {
                    var updater = new MarketDataUpdater(client, repository);
                    var outcome = await updater.UpdateAsync(symbol, start, end, backfill: backfill);
                    if (outcome.RequiresManualReview || (outcome.PriceRows == 0 && oldLatest is null))
                    {
                        errors++;
                        Console.WriteLine($"[REVIEW] {symbol}: {outcome.Message}");
                    }
                    else
                    {
                        DateOnly? latest = await repository.LatestDateAsync(symbol);
                        Console.WriteLine($"[OK / ACTION HISTORY NOT GUARANTEED] {symbol}: price rows={outcome.PriceRows}, events={outcome.ReferenceEvents}, cached latest={latest:yyyy-MM-dd}; {outcome.Message}");
                    }
                }
            }
            catch (FinMindException ex)
            {
                errors++;
                Console.Error.WriteLine($"[ERROR] {symbol}: {ex.Message} (status={ex.HttpStatus?.ToString() ?? "n/a"})");
            }
            catch (HttpRequestException ex)
            {
                errors++;
                Console.Error.WriteLine($"[NETWORK ERROR] {symbol}: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                errors++;
                Console.Error.WriteLine($"[TIMEOUT] {symbol}: network timeout or cancellation");
            }
            catch (Exception ex)
            {
                errors++;
                Console.Error.WriteLine($"[ERROR] {symbol}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        Console.WriteLine();
        Console.WriteLine($"Completed. Symbols requiring attention: {errors}/{symbols.Distinct().Count()}");
        Console.WriteLine("Important: 2025/2026 ETF splits and dividend adjustment data MUST be independently validated before trading signals/backtests.");
        return errors == 0 ? 0 : 1;
    }

    private static string GetValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length) throw new ArgumentException($"Missing value after {args[index]}");
        return args[++index];
    }
    private static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static void PrintUsage()
    {
        Console.WriteLine("dotnet run --project ETFInvestment.Collector -- [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--symbols 0050,0056,00878,00631L] [--prices-only] [--backfill]");
        Console.WriteLine("--prices-only: raw-price API check only. --backfill: refetch specified historical period instead of recent overlap.");
        Console.WriteLine("Neither mode authorizes trading signals; corporate actions must be verified independently.");
    }
}
