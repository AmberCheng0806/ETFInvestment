# ETFInvestment v2.4 — 還原價格連續性稽核（內部驗證）

完整更新 ZIP，保留 Core、Data、Collector、Tests。**不含交易訊號、Token、私人持倉**。

## 本版新增

- `ETFInvestment.Core/AdjustedPriceAudit.cs`：利用原始行情與已儲存之除息／分割事件，套用既有 `AdjustedSeries`，對可疑價格跳躍做檢查。
- `ETFInvestment.Collector --audit-only`：只讀本機 `market.db`，不呼叫網路 API，也不存取 `portfolio.db`。
- `ETFInvestment.Tests/AdjustedPriceAuditTests.cs`：6 個稽核測試，包括 00631L 22:1 分割、錯誤分割因子、缺失事件、現金股利、重複事件及資料不足。

## 操作（Windows PowerShell，Solution 根目錄）

```powershell
dotnet restore
dotnet build
dotnet test
```

前一版預期 28 個測試，此版新增 6 個，**預期 34 個**（以實際結果為準，尚未在本環境以 .NET SDK 編譯）。

你已成功回補四檔行情與事件，此版先執行：

```powershell
dotnet run --project ETFInvestment.Collector -- --from 2025-01-01 --audit-only
```

例如輸出：`[AUDIT / NOT SOURCE-VERIFIED] 00631L: prices=..., cached events=..., adjusted points=..., findings=0`。

- `findings=0` 只表示**快取資料在已知事件下通過機械連續性檢查**，不表示歷史除息／分割事件完整，更不代表股價／投資策略可靠。
- `findings>0` 會顯示日期與原因，並以非零結束碼結束。
- 股利參考價格採官方參考價因子；**歷史還原價格不是含息現金流績效**。
- 對於 0050 與 00631L 分割日期需另與交易所公告核對；00631L 2026-03-31 已使用 22:1；其餘已取得事件也要檢查完整性。
- 調整後價格序列目前只在記憶體計算，尚未對外宣稱可產生正式買點。

## 限制與後續

門檻目前採保守初步值：無已知事件的相鄰交易日價格跳動絕對值≥35%時標記；有事件而調整後跳動≥20%時標記。這些是**資料健檢參數**，不是交易策略或市場異常的絕對判定。重要缺漏仍可能通過，尤其是小額股利與長期間資料源缺口。下一步應將事件清單與發行商／TWSE 逐筆核對，並與獨立調整價來源比較，才開啟買賣訊號。

## 安全

FinMind Token 繼續留在 Collector User Secrets；`market.db` 位於 `%LOCALAPPDATA%\ETFInvestment`；不涉及 `portfolio.db`。原本 `.gitignore` 繼續使用。可覆蓋先前程式檔案，保留 `.git`。
