using System.IO;
using food.Models;
using food.Services;

namespace food.Tests
{
    /// <summary>存檔（data.json）與 Excel 匯出 / 匯入。全部寫在暫存資料夾，不會動到真正的資料。</summary>
    public sealed class StorageTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "FoodKeeper-Tests-" + Guid.NewGuid().ToString("N"));

        public StorageTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private static FoodData SampleWithRestaurants()
        {
            var data = new FoodData();
            var daan = CategoryService.EnsurePath(data, ["台灣", "台北市", "大安區"])!;
            CategoryService.EnsurePath(data, ["台灣", "台北市", "信義區"]); // 空的地區
            var tokyo = CategoryService.EnsurePath(data, ["日本", "東京都"])!;
            data.Restaurants.Add(new Restaurant
            {
                Name = "鼎泰豐", CategoryId = daan, Address = "信義路二段 194 號", Phone = "02-2321-8928",
                Rating = 5, Note = "小籠包\n要排隊", CreatedAt = new DateTime(2026, 1, 1),
            });
            data.Restaurants.Add(new Restaurant { Name = "'引號開頭", CategoryId = tokyo, CreatedAt = new DateTime(2026, 1, 2) });
            return data;
        }

        // ---------- data.json ----------

        [Fact]
        public void Json_RoundTrips()
        {
            var path = Path.Combine(_root, "data.json");
            var data = SampleWithRestaurants();

            DataStore.Save(path, data);
            var loaded = DataStore.Load(path);

            Assert.Equal(data.Categories.Select(c => (c.Id, c.Name, c.ParentId)), loaded.Categories.Select(c => (c.Id, c.Name, c.ParentId)));
            Assert.Equal(data.Restaurants.Select(r => (r.Name, r.CategoryId, r.Phone, r.Rating, r.Note)),
                loaded.Restaurants.Select(r => (r.Name, r.CategoryId, r.Phone, r.Rating, r.Note)));
            Assert.Contains("鼎泰豐", File.ReadAllText(path)); // 中文不被跳脫，打開檔案看得懂
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void Json_MissingFile_StartsWithSample()
        {
            var data = DataStore.Load(Path.Combine(_root, "none.json"));
            Assert.NotEmpty(data.Categories);
        }

        [Fact]
        public void Json_CorruptFile_IsBackedUpAndStartsEmpty()
        {
            var path = Path.Combine(_root, "data.json");
            File.WriteAllText(path, "{ 壞掉");

            var data = DataStore.Load(path);

            Assert.Empty(data.Categories);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(_root, "data.json.corrupt-*"));
        }

        // ---------- Excel ----------

        [Fact]
        public void Excel_RoundTrip_RestoresCategoriesAndRestaurants()
        {
            var path = Path.Combine(_root, "export.xlsx");
            var original = SampleWithRestaurants();

            ExcelService.Export(original, path);
            var rows = ExcelService.Import(path);
            var restored = new FoodData();
            int added = ExcelService.Apply(restored, rows, replace: true);

            Assert.Equal(2, added);
            Assert.Equal(original.Categories.Select(c => CategoryService.PathText(original, c.Id)).Order(),
                restored.Categories.Select(c => CategoryService.PathText(restored, c.Id)).Order()); // 空的「信義區」也還原
            var dtf = restored.Restaurants.Single(r => r.Name == "鼎泰豐");
            Assert.Equal("台灣 › 台北市 › 大安區", CategoryService.PathText(restored, dtf.CategoryId));
            Assert.Equal("02-2321-8928", dtf.Phone);
            Assert.Equal(5, dtf.Rating);
            Assert.Equal("小籠包\n要排隊", dtf.Note.Replace("\r\n", "\n"));
            Assert.Contains(restored.Restaurants, r => r.Name == "'引號開頭");
        }

        [Fact]
        public void Excel_Merge_SkipsSameNameInSameCategory()
        {
            var path = Path.Combine(_root, "export.xlsx");
            var data = SampleWithRestaurants();
            ExcelService.Export(data, path);

            int added = ExcelService.Apply(data, ExcelService.Import(path), replace: false);

            Assert.Equal(0, added);
            Assert.Equal(2, data.Restaurants.Count);
        }

        [Theory]
        [InlineData("4", 4)]
        [InlineData("4.6", 5)]
        [InlineData("9", 5)]
        [InlineData("★★★", 3)]
        [InlineData("", 0)]
        [InlineData("好吃", 0)]
        public void ParseRating_AcceptsNumbersOrStars(string text, int expected) =>
            Assert.Equal(expected, ExcelService.ParseRating(text));
    }
}
