namespace food.Models
{
    /// <summary>分類：國家 › 縣市 › 地區，ParentId 為 null 表示最上層的國家。</summary>
    public class Category
    {
        public string Id { get; set; } = NewId();
        public string Name { get; set; } = "";
        public string? ParentId { get; set; }

        public static string NewId() => Guid.NewGuid().ToString("N");

        public override string ToString() => Name;
    }

    public class Restaurant
    {
        public string Id { get; set; } = Category.NewId();
        public string Name { get; set; } = "";

        /// <summary>所屬分類（國家、縣市或地區都可以）。</summary>
        public string CategoryId { get; set; } = "";

        public string Address { get; set; } = "";
        public string Phone { get; set; } = "";

        /// <summary>評分 0～5，0 表示未評分。</summary>
        public int Rating { get; set; }

        public string Note { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public override string ToString() => Name;
    }

    /// <summary>存檔的全部內容（data.json）。</summary>
    public class FoodData
    {
        public List<Category> Categories { get; set; } = new();
        public List<Restaurant> Restaurants { get; set; } = new();
    }
}
