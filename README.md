# ETFInvestment v3.0｜WPF 本機市場儀表板

此 ZIP **完整保留 v2.4 的 Core、Data、Collector、Tests**，新增 `ETFInvestment.WPF`。本版僅使用公開行情快取 `market.db`，**不會讀寫 `portfolio.db`，不儲存 Token，不產生實際交易訊號**。

## 驗收步驟（Windows / Visual Studio 2022）

1. 先將 v2.4 Commit。關閉 Visual Studio，將本 ZIP 解壓覆蓋原專案（保留 `.git`）。
2. `dotnet restore`、`dotnet build`、`dotnet test`。原有測試應繼續通過（v2.4 預期34項，請依你的本機結果為準）。
3. 將 `ETFInvestment.WPF` 設定為**啟始專案**，按 F5；或執行 `dotnet run --project ETFInvestment.WPF`。
4. 首頁有0050、0056、00878、00631L，點選後應顯示收盤價、資料日期、20交易日調整價漲跌幅、歷史圖。
5. 測試期間選單（1月／3月／6月／1年／全部）、原始／調整後收盤價切換、20／120日均線勾選。
6. 本版不連網。若行情過期，先在命令列執行 `dotnet run --project ETFInvestment.Collector -- --from 2025-01-01`，再回 WPF 點「重新讀取快取」。

## 已知限制與安全標記

- 不使用 LiveCharts2；使用原生 WPF Canvas + Polyline，無新增圖表 NuGet，相容性較容易驗證。
- 顯示的20交易日變化，是**調整後價格比例**，不是價格原始漲跌、也不是投資帳戶含現金流的報酬率。
- `AdjustedPriceAudit` 通過只代表機械連續性檢查通過，不代表歷史事件完整；UI 持續顯示「來源未驗證」，不提供買賣提示。
- 缺少快取或資料錯誤時，介面明確顯示錯誤，不會自動建立私人資料庫。
- 日線價格只更新到已下載的交易日；不提供盤中即時行情。
- WPF 需要 Windows；目前執行環境無法使用 .NET SDK 編譯或呈現 WPF，所以**必須由你在 Visual Studio 真正編譯與驗收畫面**。
- 下階段 v3.1 才加入私人交易記錄、配息、正2基準，以及在來源驗證完成後的候選買點。
