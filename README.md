# ETFInvestment v2.3：00631L 官方分割事件辨識修正

這是完整更新版（Core、Data、Tests、Collector）。保留前一版的所有程式與測試。

## 修正原因

v2.2 真實回補顯示 `00631L` 在 `2026-03-31` 有分割資料，但 FinMind API 回傳的 `type` 不符合舊版只接受含「分割」文字的辨識條件，導致 `[REVIEW]`。

根據臺灣證券交易所於 2026-03-30 公布的元大投信正式公告：00631L 在 2026-03-31 以 **22:1** 分割後恢復交易；分割前最後收盤價 **443.15 元**，分割後參考價 **20.14 元**。官方公告：https://www.twse.com.tw/zh/ETFortune/announcement?company=A00005&date=20260330&fund=00631L&seq=1&type=all

## 修改範圍

- `ETFInvestment.Data/FinMind/FinMindClient.cs`：未知的 split `type` 不再一律忽略；**只**在 00631L、2026-03-31、before≈443.15、after≈20.14、近似 22:1 全部符合時，依已核對的官方公告接納該事件；其他未知事件仍擋下待人工檢查。該日事件的調整因子用精確 `1/22`，避免參考價四捨五入造成偏差。
- `ETFInvestment.Tests/FinMindAndSqliteTests.cs`：新增 4 項測試，確認官方事件可識別、不符數字或日期的未知事件不能放行，以及正常「分割」標籤仍可處理。
- `ETFInvestment.Collector/Program.cs`：版本顯示更新為 v2.3。

## Windows 驗收步驟

1. 先 Commit 前一版，關閉 VS，再將 ZIP 內容覆蓋專案（保留 `.git`）。
2. 在 Solution 根目錄執行：

```powershell
dotnet restore
dotnet build
dotnet test
```

原 v2.2 預期 24 項測試，本次新增 4 項，**預期 28 項**，最終以實際 Test Explorer 結果為準。這份程式未在本環境進行 .NET 編譯。

3. 下載真實 00631L 資料並確認能辨識分割：

```powershell
dotnet run --project ETFInvestment.Collector -- --symbols 00631L --from 2025-01-01 --backfill
```

4. 若成功，再對四檔驗證：

```powershell
dotnet run --project ETFInvestment.Collector -- --from 2025-01-01 --backfill
```

期望 00631L 顯示 `split events=1`、`Symbols requiring attention: 0/4`。如果來源的實際 `before_price` / `after_price` 或 `type` 不符合預期，程式仍會顯示 `[REVIEW]` 並輸出**不含 Token**的事件數字；請回傳錯誤文字，不要硬改常數，也不要直接啟用買賣訊號。

## 尚未完成驗證

本版只修正**特定已公告的分割事件辨識**，尚未確認四檔 ETF 的全部除息事件、全期調整後序列及價格回檔訊號是否正確。Collector 仍為驗證工具，**不自動交易、也不提供正式買點**。

FinMind Token 仍儲存於 Collector 的 User Secrets 或環境變數；ZIP 不含 Token、portfolio.db 或其他個人資料。
