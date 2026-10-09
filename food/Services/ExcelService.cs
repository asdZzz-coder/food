using System.IO;
using ClosedXML.Excel;
using food.Models;

namespace food.Services
{
    /// <summary>Excel 的一列：分類路徑（國家 / 縣市 / 地區）＋ 餐廳；餐廳名稱空白的列只用來建立分類。</summary>
    public record ImportRow(string Country, string City, string District, Restaurant? Restaurant);

    /// <summary>
    /// Excel 匯出 / 匯入。欄位：國家、縣市、地區、餐廳名稱、地址、電話、評分、備註。
    /// 沒有餐廳的分類也會匯出成一列（餐廳名稱空白），匯入時才能把空分類一起還原。
    /// 也可以自己在 Excel 依同樣欄位大量輸入，再匯入。
    /// </summary>
    public static class ExcelService
    {
        public static readonly string[] Headers = { "國家", "縣市", "地區", "餐廳名稱", "地址", "電話", "評分", "備註" };

        public static void Export(FoodData data, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("餐廳");
            for (int c = 0; c < Headers.Length; c++)
                ws.Cell(1, c + 1).Value = Headers[c];
            ws.Row(1).Style.Font.Bold = true;

            int r = 2;
            foreach (var row in Rows(data))
            {
                ws.Cell(r, 1).SetValue(Escape(row.Country));
                ws.Cell(r, 2).SetValue(Escape(row.City));
                ws.Cell(r, 3).SetValue(Escape(row.District));
                if (row.Restaurant is { } x)
                {
                    ws.Cell(r, 4).SetValue(Escape(x.Name));
                    ws.Cell(r, 5).SetValue(Escape(x.Address));
                    ws.Cell(r, 6).SetValue(Escape(x.Phone)); // 一律寫成文字，避免電話開頭的 0 被吃掉
                    if (x.Rating > 0) ws.Cell(r, 7).SetValue(x.Rating);
                    ws.Cell(r, 8).SetValue(Escape(x.Note));
                    ws.Cell(r, 8).Style.Alignment.WrapText = true;
                }
                r++;
            }
            ws.Columns().AdjustToContents();
            foreach (var col in ws.ColumnsUsed())
                if (col.Width > 50) col.Width = 50;
            ws.SheetView.FreezeRows(1);
            wb.SaveAs(path);
        }

        /// <summary>要匯出的每一列：先依分類順序列出餐廳，沒有任何餐廳的最底層分類也列一行。</summary>
        internal static List<ImportRow> Rows(FoodData data)
        {
            var rows = new List<ImportRow>();
            void Walk(Category c)
            {
                var path = CategoryService.PathOf(data, c.Id).Select(p => p.Name).ToList();
                string At(int i) => i < path.Count ? path[i] : "";
                var own = data.Restaurants.Where(x => x.CategoryId == c.Id).OrderBy(x => x.CreatedAt).ToList();
                foreach (var x in own) rows.Add(new ImportRow(At(0), At(1), At(2), x));

                var children = CategoryService.Children(data, c.Id).ToList();
                if (own.Count == 0 && children.Count == 0) rows.Add(new ImportRow(At(0), At(1), At(2), null));
                foreach (var child in children) Walk(child);
            }
            foreach (var country in CategoryService.Children(data, null).ToList()) Walk(country);
            return rows;
        }

        // 開頭的 ' 會被 ClosedXML 當成 Excel 的「文字前綴」而吞掉，多加一個才能原樣保留
        private static string Escape(string s) => s.StartsWith('\'') ? "'" + s : s;

        public static List<ImportRow> Import(string path)
        {
            // 允許讀取「正在被 Excel 開啟」的檔案，否則會因檔案被鎖定而失敗
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.FirstOrDefault();
            if (ws == null) return new();
            var result = new List<ImportRow>();
            foreach (var row in ws.RowsUsed().Skip(1)) // 跳過標題列
            {
                string Text(int c) => row.Cell(c).GetFormattedString().Trim();
                var country = Text(1);
                if (country.Length == 0) continue; // 沒有國家就沒辦法分類
                var name = Text(4);
                Restaurant? restaurant = name.Length == 0 ? null : new Restaurant
                {
                    Name = name,
                    Address = Text(5),
                    Phone = Text(6),
                    Rating = ParseRating(Text(7)),
                    Note = row.Cell(8).GetFormattedString().Trim(),
                };
                result.Add(new ImportRow(country, Text(2), Text(3), restaurant));
            }
            return result;
        }

        /// <summary>評分可以填數字（1～5）或星星（★★★）。</summary>
        internal static int ParseRating(string text)
        {
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
                return Math.Clamp((int)Math.Round(n), 0, 5);
            return Math.Clamp(text.Count(ch => ch == '★'), 0, 5);
        }

        /// <summary>
        /// 把匯入的資料併進 data：分類依名稱比對（沒有就建立）；同一個分類裡已有同名餐廳的略過。
        /// replace 為 true 時先清空原本所有資料。回傳新增的餐廳數。
        /// </summary>
        public static int Apply(FoodData data, IEnumerable<ImportRow> rows, bool replace)
        {
            if (replace)
            {
                data.Categories.Clear();
                data.Restaurants.Clear();
            }
            int added = 0;
            foreach (var row in rows)
            {
                var categoryId = CategoryService.EnsurePath(data, new[] { row.Country, row.City, row.District });
                if (categoryId == null || row.Restaurant is not { } x) continue;
                bool exists = data.Restaurants.Any(r => r.CategoryId == categoryId &&
                    string.Equals(r.Name, x.Name, StringComparison.OrdinalIgnoreCase));
                if (exists) continue;
                x.CategoryId = categoryId;
                data.Restaurants.Add(x);
                added++;
            }
            return added;
        }
    }
}
