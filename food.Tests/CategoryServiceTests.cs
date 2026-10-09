using food.Models;
using food.Services;

namespace food.Tests
{
    /// <summary>三層分類（國家 › 縣市 › 地區）的新增、改名、刪除規則。</summary>
    public class CategoryServiceTests
    {
        private static (FoodData Data, Category Tw, Category Tp, Category Daan) Build()
        {
            var data = new FoodData();
            var tw = CategoryService.Add(data, null, "台灣");
            var tp = CategoryService.Add(data, tw.Id, "台北市");
            var daan = CategoryService.Add(data, tp.Id, "大安區");
            return (data, tw, tp, daan);
        }

        private static Restaurant AddRestaurant(FoodData data, Category c, string name)
        {
            var r = new Restaurant { Name = name, CategoryId = c.Id };
            data.Restaurants.Add(r);
            return r;
        }

        // ---------- 新增 ----------

        [Fact]
        public void Add_BuildsThreeLevels()
        {
            var (data, tw, tp, daan) = Build();

            Assert.Equal(0, CategoryService.Level(data, tw.Id));
            Assert.Equal(1, CategoryService.Level(data, tp.Id));
            Assert.Equal(2, CategoryService.Level(data, daan.Id));
            Assert.Equal("台灣 › 台北市 › 大安區", CategoryService.PathText(data, daan.Id));
            Assert.Equal("地區", CategoryService.LevelName(2));
        }

        [Fact]
        public void Add_BelowDistrict_IsRejected()
        {
            var (data, _, _, daan) = Build();
            Assert.Throws<InvalidOperationException>(() => CategoryService.Add(data, daan.Id, "某某里"));
        }

        [Fact]
        public void Add_TrimsName()
        {
            var data = new FoodData();
            Assert.Equal("日本", CategoryService.Add(data, null, "  日本 ").Name);
        }

        [Theory]
        [InlineData("", CategoryNameError.Empty)]
        [InlineData("   ", CategoryNameError.Empty)]
        [InlineData("台北市", CategoryNameError.Duplicate)]
        [InlineData(" 台北市 ", CategoryNameError.Duplicate)]
        [InlineData("新北市", CategoryNameError.None)]
        public void Validate_UnderSameParent(string name, CategoryNameError expected)
        {
            var (data, tw, _, _) = Build();
            Assert.Equal(expected, CategoryService.Validate(data, tw.Id, name));
        }

        [Fact]
        public void Validate_SameNameUnderDifferentParent_IsAllowed()
        {
            var (data, tw, tp, _) = Build();
            var ntp = CategoryService.Add(data, tw.Id, "新北市");
            CategoryService.Add(data, ntp.Id, "中正區");
            Assert.Equal(CategoryNameError.None, CategoryService.Validate(data, tp.Id, "中正區"));
        }

        [Fact]
        public void Validate_CaseInsensitiveAndTooLong()
        {
            var data = new FoodData();
            CategoryService.Add(data, null, "Japan");
            Assert.Equal(CategoryNameError.Duplicate, CategoryService.Validate(data, null, "JAPAN"));
            Assert.Equal(CategoryNameError.TooLong, CategoryService.Validate(data, null, new string('字', CategoryService.MaxNameLength + 1)));
        }

        // ---------- 重新命名 ----------

        [Fact]
        public void Rename_KeepsRestaurants()
        {
            var (data, _, tp, _) = Build();
            var r = AddRestaurant(data, tp, "鼎泰豐");

            CategoryService.Rename(data, tp.Id, "臺北市");

            Assert.Equal("台灣 › 臺北市", CategoryService.PathText(data, r.CategoryId));
        }

        [Fact]
        public void Rename_ToSameNameDifferentCase_IsAllowed()
        {
            var data = new FoodData();
            var c = CategoryService.Add(data, null, "japan");
            CategoryService.Rename(data, c.Id, "Japan");
            Assert.Equal("Japan", c.Name);
        }

        // ---------- 查詢 ----------

        [Fact]
        public void RestaurantsUnder_IncludesDescendants()
        {
            var (data, tw, tp, daan) = Build();
            var a = AddRestaurant(data, tp, "A");
            var b = AddRestaurant(data, daan, "B");
            var jp = CategoryService.Add(data, null, "日本");
            AddRestaurant(data, jp, "C");

            Assert.Equal([a, b], CategoryService.RestaurantsUnder(data, tw.Id));
            Assert.Equal([b], CategoryService.RestaurantsUnder(data, daan.Id));
            Assert.Equal(3, CategoryService.RestaurantsUnder(data, null).Count());
        }

        [Fact]
        public void PathOf_SurvivesBrokenParentLoop()
        {
            var data = new FoodData();
            data.Categories.Add(new Category { Id = "a", Name = "A", ParentId = "b" });
            data.Categories.Add(new Category { Id = "b", Name = "B", ParentId = "a" });
            Assert.Equal(2, CategoryService.PathOf(data, "a").Count); // 不會無窮迴圈
        }

        // ---------- 刪除 ----------

        [Fact]
        public void Delete_District_MovesRestaurantsToCity()
        {
            var (data, _, tp, daan) = Build();
            var r = AddRestaurant(data, daan, "鼎泰豐");

            var result = CategoryService.Delete(data, daan.Id);

            Assert.Equal(tp.Id, r.CategoryId);
            Assert.Equal(new DeleteResult(1, 1, 0, tp), result);
            Assert.Null(CategoryService.Get(data, daan.Id));
            Assert.Single(data.Restaurants);
        }

        [Fact]
        public void Delete_City_RemovesDistrictsAndMovesAllRestaurantsToCountry()
        {
            var (data, tw, tp, daan) = Build();
            var a = AddRestaurant(data, tp, "A");
            var b = AddRestaurant(data, daan, "B");

            var result = CategoryService.Delete(data, tp.Id);

            Assert.Equal(2, result.Categories);
            Assert.Equal(2, result.Moved);
            Assert.Equal([tw], data.Categories);
            Assert.All(new[] { a, b }, r => Assert.Equal(tw.Id, r.CategoryId));
        }

        [Fact]
        public void Delete_Country_RemovesEverythingUnderIt()
        {
            var (data, tw, _, daan) = Build();
            AddRestaurant(data, daan, "B");
            var jp = CategoryService.Add(data, null, "日本");
            var sushi = AddRestaurant(data, jp, "壽司");

            var result = CategoryService.Delete(data, tw.Id);

            Assert.Equal(new DeleteResult(3, 0, 1, null), result);
            Assert.Equal([jp], data.Categories);
            Assert.Equal([sushi], data.Restaurants);
        }

        [Fact]
        public void Preview_DoesNotChangeData()
        {
            var (data, tw, _, daan) = Build();
            AddRestaurant(data, daan, "B");

            var preview = CategoryService.Preview(data, tw.Id);

            Assert.Equal(1, preview.Deleted);
            Assert.Equal(3, data.Categories.Count);
            Assert.Single(data.Restaurants);
        }

        // ---------- 匯入用：依名稱建立路徑 ----------

        [Fact]
        public void EnsurePath_ReusesExistingAndCreatesMissing()
        {
            var (data, _, tp, daan) = Build();

            Assert.Equal(daan.Id, CategoryService.EnsurePath(data, ["台灣", "台北市", "大安區"]));
            Assert.Equal(tp.Id, CategoryService.EnsurePath(data, ["台灣", " 台北市 ", ""]));

            var id = CategoryService.EnsurePath(data, ["台灣", "台北市", "信義區"]);
            Assert.Equal("台灣 › 台北市 › 信義區", CategoryService.PathText(data, id));
            Assert.Equal(4, data.Categories.Count);
        }

        [Fact]
        public void EnsurePath_StopsAtFirstBlankAndNeedsCountry()
        {
            var data = new FoodData();
            Assert.Null(CategoryService.EnsurePath(data, ["", "台北市"]));
            var id = CategoryService.EnsurePath(data, ["日本", "", "澀谷"]);
            Assert.Equal("日本", CategoryService.PathText(data, id));
        }

        [Fact]
        public void Sample_HasThreeLevels()
        {
            var data = CategoryService.Sample();
            Assert.Contains(data.Categories, c => CategoryService.Level(data, c.Id) == 2);
            Assert.Empty(data.Restaurants);
        }
    }
}
