# ETFInvestment — 第一階段（Core + Tests）

本階段交付純 .NET 8 核心計算、資料介面與 xUnit 單元測試，**尚無 WPF、FinMind API、SQLite 或自動下單**。

## 在 Windows / Visual Studio 2022 驗收

1. 安裝 .NET 8 SDK、Visual Studio 2022（含 .NET 桌面開發）。
2. 解壓縮 ZIP，開啟 `ETFInvestment.sln`。
3. Visual Studio：右鍵 Solution → Restore NuGet Packages；Build → Build Solution。
4. Test → Test Explorer → Run All Tests。
5. 或在 PowerShell：

```powershell
dotnet --version
dotnet restore .\ETFInvestment.sln
dotnet build .\ETFInvestment.sln --no-restore
dotnet test .\ETFInvestment.sln --no-build
```

**驗收門檻**：0 編譯錯誤、所有測試 Passed、0 Failed、無非預期 Skipped。請回傳測試摘要或錯誤輸出，才能開始第二階段。

## 已涵蓋的純計算

- `AdjustedSeries`: 依已確認參考價計算向後調整因子、除息與分割事件。Core 不解析 FinMind JSON。
- `Indicators`: 移動平均、調整後價格高點回檔、歷史交易日筆數。
- `CoreEtfStrategy`: 首筆回檔 / 20 交易日到期半筆、後續跌 5% / 20 日、最終截止日提示與買得起的零股數。
- `LeveragedEtfStrategy`: 00631L 對最後一次**實際成交後**的基準價達 ±50% 時，計算 50:50 再平衡金額。
- `IRepositories`: 第二階段 Data 將實作的介面。

## 關鍵行為與限制

1. 策略僅輸出 `BuySignal` / `RebalanceSignal`，**不會下單，也不會更動個人持股**。
2. 首筆及後續買入門檻、20交易日、12個月期限都是**待回測**的實驗參數，不是已驗證有效的投資策略。
3. **Core 的成交基準價格必須先調整到當前相同的除息／分割尺度**：後續加碼呼叫 `EvaluateSubsequent(..., adjustedLastBuyPrice)`，不能把當時原始買價直接與當前還原價比較。第一階段刻意對缺少此輸入回傳 `AwaitingData`，避免假訊號；第二階段才由 Data 提供完整歷史事件並整合計算。
4. 因資料來源不確定，對未確認分割/價格異常，未來 Data 應停止產生訊號並標記 `AwaitingData`；Core 此階段不會自動偵測所有異常單日漲跌。
5. 買入數量用**當前收盤價**估算可買股數，尚未包含隔日成交價差、交易稅（買入一般不課證交稅）、最低手續費實際規定及下單單位限制，正式交易前應重算。
6. 初始等待 20 日後「半筆」是初始**目標股數**的一半。若 ETF 價格過高會自動縮為可買股數；首筆初始張數以 `TargetInitialShares` 設定。
7. 12 個月期滿訊號可以超過每筆 2000 股上限，因為「到期買完剩餘預算」與單筆上限衝突；這是**明確的規格例外**。程式僅提出建議，不會成交。
8. 正2再平衡金額按零成本、可買小數股進行理論計算，**第二階段須實作交易成本與整股/零股數量校正**。實際成交後才重設基準價。
9. 實際投入金額、規則開始日期、私人成交與股息資料將於第二階段存於 `%LOCALAPPDATA%\ETFInvestment\portfolio.db`，**不是此專案的 example 設定檔**。
10. 目前沒有串接 FinMind，也沒有進行真實市場回測；xUnit 通過只代表這些指定案例的計算符合預期。

## Git 安全性

可以提交 `.sln`、`.csproj`、`.cs`、`README.md`、`appsettings.example.json`、`.gitignore`。
**不要**提交 Token、實際家人資產、個人成交 CSV、SQLite 檔案、User Secrets、`bin/`/`obj/`。

## 後續第二階段（完成本階段驗收後才動工）

`ETFInvestment.Data` (.NET 8)：FinMind HttpClient/JSON 映射、增量更新、還原調整事件確認、`market.db` / `portfolio.db` 分庫、SQLite 一致備份、對應測試。此階段需你在 Windows 上驗證真實連線與 NuGet 套件編譯。
