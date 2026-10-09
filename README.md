# 餐廳收藏

收藏餐廳的 Windows 桌面程式（.NET 10 WPF）。分類分成三層：**國家 › 縣市 › 地區**，可以隨時手動新增、重新命名、刪除。

## 下載安裝

1. 到 [Releases](https://github.com/asdZzz-coder/food/releases/latest) 下載 `FoodKeeper-ClickOnce.zip`
2. 解壓縮後執行「安裝.cmd」
3. 安裝完成後，開始功能表與桌面會有「餐廳收藏」捷徑

不需要另外安裝 .NET（已內建執行環境）。之後有新版時，開啟程式就會詢問是否更新，也可以按右上角「檢查更新」。

## 功能

- **分類**：左側樹狀清單。「新增國家」在左上角；滑過分類（或按右鍵）可以「新增下一層 / 重新命名 / 刪除」；雙擊可展開或收合
- **刪除分類**：底下的子分類一起刪除，餐廳移到上一層；刪除國家時，底下的餐廳也會一起刪除（刪除前會先確認）
- **餐廳**：名稱、國家 / 縣市 / 地區、地址（可直接開 Google 地圖）、電話、評分、備註；可以放在任一層分類
- 點上層分類時，會列出底下所有子分類的餐廳；可以搜尋名稱、地址、電話、備註
- **Excel 匯出 / 匯入**：欄位為「國家、縣市、地區、餐廳名稱、地址、電話、評分、備註」，也可以自己在 Excel 依這個格式大量輸入後匯入
- 淺色 / 深色 / 跟隨系統主題

資料存在 `%AppData%\FoodKeeper\data.json`。換電腦時用「匯出 Excel」再「匯入 Excel」，或直接複製這個檔案。

## 開發

```bash
dotnet build food/food.csproj
dotnet test food.Tests/food.Tests.csproj
```

### 發佈新版

推送 `v` 開頭的標籤後，GitHub Actions 會先跑測試，再用 ClickOnce 打包並上傳到 Releases：

```bash
git tag v1.0.1
git push origin v1.0.1
```
