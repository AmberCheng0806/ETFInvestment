# ETFInvestment — 第二階段 Data + Core + Tests

**這是完整更新版 ZIP**，包含第一階段 Core / 15 個原測試，以及第二階段 Data / 6 個新測試。目前尚無 WPF、尚未執行真實 FinMind API 回測、也沒有自動下單。

## 開啟與驗證

1. 解壓縮並開啟 `ETFInvestment.sln`；本階段共有 Core、Data、Tests 三個專案。
2. 安裝 .NET 8 SDK，還原 NuGet：`dotnet restore ETFInvestment.sln`。Data 會安裝 `Microsoft.Data.Sqlite 8.0.11`。
3. `dotnet build ETFInvestment.sln --no-restore`
4. `dotnet test ETFInvestment.sln --no-build`。
5. **預期**：21 個測試全部通過（原15 + 新6）。首次請把測試結果貼回來，若有錯先修正。

本 ZIP 在交付環境可檢查檔案與程式碼，但**無 .NET SDK / NuGet 網路環境，未實際編譯 xUnit**；上述是預期測試數，不是已驗證結果。

## 第二階段新增內容

- `FinMindClient`：使用 `HttpClient` + `System.Text.Json`，`TaiwanStockPrice` 日價與 `TaiwanStockDividendResult` 參考價；Token 由外部注入、只放 Authorization Bearer header。HTTP 429/5xx 有有限次重試，401/403/4xx 不自動重試。
- `MarketDataUpdater`：最後日期起往前重抓 7 個日曆日以覆蓋遲到/更正資料；每日價格 upsert，不產生重複資料。
- `market.db` / `portfolio.db`：兩個獨立 SQLite 檔，預設在 `%LOCALAPPDATA%\ETFInvestment\`；目前建立資料表並提供程式 API，**尚未提供 UI 匯入或輸入 Token 功能**。
- `SqliteDatabase.BackupPortfolioAsync`：使用 SQLite 官方連線備份 API，在 WAL 模式下產生一致快照。
- 私人資料：策略起始日期、期限、成交、配息、00631L 最新再平衡基準；只記錄使用者確認過的事實。

## 資料核實與風險限制（請務必閱讀）

1. `TaiwanStockDividendResult` 的參考價**不能保證涵蓋 ETF 分割**。任何非現金事件或單日價格跳動 >=35% 先停止更新、要求人工核對；這只是初步哨兵，無法攔住所有資料異常。需要後續另外建立已確認分割事件手動匯入與接受流程。
2. **本版不應用作正式買賣訊號**：未建立完整公司行動資料鏈、不保證免費帳號能存取所有資料集、沒有真實行情端到端測試。無調整事件也不能推論「確定不存在分割」。
3. `CoreEtfStrategy` 後續加碼仍必須提供已調整至相同價格尺度的實際成交參考價；Data 本版**尚未封裝此整合**，絕不能直接把原始成交價與還原價比較。
4. `MarketDataUpdater` 目前針對價格和調整事件使用兩步 upsert，不是跨表原子交易。如果任一步異常，可能留下部分已更新的資料。此版本只能用於資料層測試；正式提示前要改為同一 transaction 並增加完整性檢查。
5. `PortfolioRepository.GetCoreStateAsync` 中 `Spent` 現採「買進成本 - 賣出收入」的**現金流近似**。核心 ETF 正常規則不主動賣出，暫可供測試；若加入賣出/費用/拆股等情境，須先明確定義預算占用與成本法，不能直接用於正式績效。
6. 備份含敏感持股資訊，備份檔必須存在私有目錄並加密保護。Git 不應提交 `.db`、真實 CSV、Token。
7. 公開行情模式與家人私人資產模式應分開。不要將真實 `portfolio.db` 複製到公司電腦。

## 如何手動測試真實 API？

此 ZIP 仍然是 Data **類別庫**，沒有 CLI/WPF 執行入口。你可以在下一階段 WPF 介面串接，或自行暫建一個 Console 專案呼叫：

```csharp
var client = new ETFInvestment.Data.FinMind.FinMindClient(new HttpClient(), token);
var prices = await client.GetPricesAsync("0050", new DateOnly(2026, 10, 1), DateOnly.FromDateTime(DateTime.Today));
Console.WriteLine($"0050 筆數：{prices.Count}");
```

為避免直接在程式寫機密 Token，實際測試請改成從環境變數或 User Secrets 注入。若沒有 Token，可先測試匿名請求（取決於 FinMind 當時規則）。

## Git 更新方式

保留原本 Repository 的 `.git` 資料夾，把本 ZIP 檔案覆蓋舊檔，利用 `git status` 檢查差異。ZIP **不含** `.git`、Token、實際持倉、資料庫檔案。

## 第二階段修正（Windows SQLite 檔案鎖定）

Windows 測試時若三項 SQLite 測試拋出 `IOException`，原因可能是 `Microsoft.Data.Sqlite` 預設連線池保留資料庫檔案控制代碼。已於 `SqliteDatabase.OpenAsync` 以及 `BackupPortfolioAsync` 的來源及目的地連線字串設定 `Pooling=false`，使 `await using` 結束後即釋放底層連線，讓測試可以刪除暫存資料庫。這是本機少量日資料應用的簡化取捨；未來如需連線池效能，可改採測試清理前 `SqliteConnection.ClearAllPools()` 或進一步調整連線生命週期。

**本環境尚未安裝 .NET SDK，請在 Windows Visual Studio 執行 21 項測試驗證。**
